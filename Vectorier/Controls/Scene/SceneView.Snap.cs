using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Vectorier.Models.Scene;
using Vectorier.Services.Localization;

namespace Vectorier.Controls.Scene;

/// <summary>
/// Port of the Unity editor's TransformSnap: hold V (snap to everything) or C (Platforms only), hover the corner of
/// the selection to grab, then drag: that corner snaps to the nearest corner of other elements (within 50 units),
/// otherwise to the nearest edge (within 200 units). Holding A scales a single element from the grabbed outer corner
/// instead, keeping the opposite corner fixed and snapping along the dominant drag axis.
/// </summary>
public sealed partial class SceneView
{
    private const double VertexSnapRadius = 50;
    private const double BoundsSearchThreshold = 200;
    private const double MinSnapPixels = 8;

    private enum SnapKind { None, Normal, Platform }

    private SnapKind _snapKind;
    private bool _snapAHeld;
    private bool _hasSnapSource;
    private Point _snapSource;
    private Point _snapSourceStart;
    private Point? _snapTarget;
    private bool _snapTargetCorner;

    // Scale snapping (A).
    private Point _scaleFixed;
    private Vector _scaleGrabDelta;

    public bool IsSnapping => _snapKind != SnapKind.None;

    /// <summary>V / C pressed with the pointer over the Scene. Returns true when snapping started.</summary>
    public bool BeginSnapKey(bool platforms)
    {
        if (_editor == null || _renderer == null || _snapKind != SnapKind.None || !_pointerInside || IsModal || _mode != InteractionMode.None)
            return false;
        if (!_editor.HasSelection || _editor.PlacementType != null)
            return false;
        if (platforms && _editor.MoveVisualizer.IsPlacementActive)
            return false; // the Move Visualizer uses C for its own platform snap
        _snapKind = platforms ? SnapKind.Platform : SnapKind.Normal;
        _snapTarget = null;
        if (!PickSnapSource(_lastPointer))
        {
            _snapKind = SnapKind.None;
            return false;
        }
        _editor.StatusText = platforms
            ? Loc.T("Platform snap: drag the selection by the highlighted corner onto a Platform (hold A to scale).")
            : Loc.T("Vertex snap: drag the selection by the highlighted corner onto another element (hold A to scale).");
        InvalidateVisual();
        return true;
    }

    /// <summary>V / C released: snapping ends (a drag in progress is kept).</summary>
    public void EndSnapKey(bool platforms)
    {
        if (_snapKind != (platforms ? SnapKind.Platform : SnapKind.Normal))
            return;
        if (_mode is InteractionMode.SnapMove or InteractionMode.SnapScale)
            FinishSnapDrag();
        _snapKind = SnapKind.None;
        _hasSnapSource = false;
        _snapTarget = null;
        InvalidateVisual();
    }

    /// <summary>The A modifier switches between moving and scaling.</summary>
    public void SetSnapScaleModifier(bool held)
    {
        if (_snapAHeld == held)
            return;
        _snapAHeld = held;
        if (_snapKind != SnapKind.None && _mode == InteractionMode.None)
        {
            PickSnapSource(_lastPointer);
            InvalidateVisual();
        }
    }

    private bool CanScaleSnap => _snapAHeld && _editor != null && _editor.GetTopmostSelection().Count == 1;

    /// <summary>Corners the selection can be grabbed by: every visible part, or only the element's outer box when scaling.</summary>
    private List<Point[]> SourceShapes()
    {
        var shapes = new List<Point[]>();
        if (_editor == null || _renderer == null)
            return shapes;
        var targets = _editor.GetTopmostSelection();
        if (CanScaleSnap)
        {
            shapes.Add(_renderer.ScreenCorners(Viewport, targets[0]));
            return shapes;
        }
        foreach (var t in targets)
            _renderer.CollectOwnShapes(Viewport, t, _snapKind == SnapKind.Platform, shapes);
        return shapes;
    }

    private bool PickSnapSource(Point pos)
    {
        double best = double.MaxValue;
        Point found = default;
        foreach (var shape in SourceShapes())
        {
            foreach (var c in shape)
            {
                double d = Distance(pos, c);
                if (d < best)
                {
                    best = d;
                    found = c;
                }
            }
        }
        _hasSnapSource = best < double.MaxValue;
        if (_hasSnapSource)
            _snapSource = found;
        return _hasSnapSource;
    }

    /// <summary>Hover while V / C is held: the nearest corner of the selection is the one that will be dragged.</summary>
    private bool UpdateSnapHover(Point pos)
    {
        if (_snapKind == SnapKind.None)
            return false;
        PickSnapSource(pos);
        _snapTarget = FindSnap(pos, out _snapTargetCorner) is { } t && t != pos ? t : null;
        Cursor = null;
        InvalidateVisual();
        return true;
    }

    private bool TryStartSnapDrag(Point pos)
    {
        if (_editor == null || _renderer == null || _snapKind == SnapKind.None)
            return false;
        PickSnapSource(pos);
        if (!_hasSnapSource)
            return false;
        _targets = _editor.GetTopmostSelection();
        if (_targets.Count == 0)
            return false;
        _editor.BeginGesture();
        _memento = new TransformMemento(_targets);
        _snapSourceStart = _snapSource;
        _pressScreen = pos;
        _dragStarted = true;

        if (CanScaleSnap)
        {
            var corners = _renderer.ScreenCorners(Viewport, _targets[0]);
            int index = 0;
            for (int i = 1; i < corners.Length; i++)
            {
                if (Distance(corners[i], _snapSource) < Distance(corners[index], _snapSource))
                    index = i;
            }
            _scaleFixed = corners[(index + corners.Length / 2) % corners.Length];
            _snapSourceStart = _snapSource = corners[index];
            _scaleGrabDelta = _snapSource - _scaleFixed;
            _mode = InteractionMode.SnapScale;
        }
        else
        {
            _mode = InteractionMode.SnapMove;
        }
        return true;
    }

    private void ApplySnap(Point pos)
    {
        if (_editor == null || _memento == null)
            return;
        _memento.Restore();
        var target = FindSnap(pos, out _snapTargetCorner);
        _snapTarget = target == pos ? null : target;

        if (_mode == InteractionMode.SnapScale)
        {
            // Lock to the axis the pointer is mostly moving along, like Unity.
            var intent = pos - _snapSourceStart;
            double sx = 1, sy = 1;
            if (Math.Abs(intent.X) > Math.Abs(intent.Y))
                sx = Math.Abs(_scaleGrabDelta.X) > 0.0001 ? (target.X - _scaleFixed.X) / _scaleGrabDelta.X : 1;
            else
                sy = Math.Abs(_scaleGrabDelta.Y) > 0.0001 ? (target.Y - _scaleFixed.Y) / _scaleGrabDelta.Y : 1;
            ScaleTargets(_scaleFixed, sx, sy, true);
            _snapSource = new Point(_scaleFixed.X + _scaleGrabDelta.X * sx, _scaleFixed.Y + _scaleGrabDelta.Y * sy);
            _editor.StatusText = Loc.F("Scale snap {0:0.###} × {1:0.###}", sx, sy);
        }
        else
        {
            var delta = target - _snapSourceStart;
            foreach (var t in _targets)
            {
                double k = PixelsToLevel(t);
                var (x0, y0) = _memento.StartPosition(t);
                t.X = x0 + delta.X * k;
                t.Y = y0 + delta.Y * k;
                if (t.IntegerTransform)
                {
                    t.X = Math.Round(t.X);
                    t.Y = Math.Round(t.Y);
                }
            }
            _snapSource = _snapSourceStart + delta;
        }
        InvalidateVisual();
    }

    private void FinishSnapDrag()
    {
        bool dragging = _mode is InteractionMode.SnapMove or InteractionMode.SnapScale;
        _mode = InteractionMode.None;
        _memento = null;
        _snapTarget = null;
        if (dragging)
            _editor?.EndGesture();
        InvalidateVisual();
    }

    /// <summary>Nearest corner within 50 units, else nearest edge point within 200 units, else the pointer.</summary>
    private Point FindSnap(Point pos, out bool corner)
    {
        corner = false;
        if (_renderer == null || _editor == null)
            return pos;
        double cornerRadius = Math.Max(MinSnapPixels, VertexSnapRadius * _zoom);
        double edgeRadius = Math.Max(MinSnapPixels, BoundsSearchThreshold * _zoom);
        var targets = _mode is InteractionMode.SnapMove or InteractionMode.SnapScale ? _targets : _editor.GetTopmostSelection();

        var shapes = new List<Point[]>();
        _renderer.CollectSnapShapes(Viewport, pos, edgeRadius, _snapKind == SnapKind.Platform, item => BelongsTo(item, targets), shapes);

        double bestCorner = double.MaxValue, bestEdge = double.MaxValue;
        Point cornerPoint = pos, edgePoint = pos;
        foreach (var shape in shapes)
        {
            for (int i = 0; i < shape.Length; i++)
            {
                var a = shape[i];
                var b = shape[(i + 1) % shape.Length];
                double d = Distance(pos, a);
                if (d < bestCorner)
                {
                    bestCorner = d;
                    cornerPoint = a;
                }
                var e = ClosestOnSegment(a, b, pos);
                double de = Distance(pos, e);
                if (de < bestEdge)
                {
                    bestEdge = de;
                    edgePoint = e;
                }
            }
        }
        if (bestCorner <= cornerRadius)
        {
            corner = true;
            return cornerPoint;
        }
        return bestEdge <= edgeRadius ? edgePoint : pos;
    }

    private static bool BelongsTo(RenderItem item, List<SceneElement> roots)
    {
        var element = item.Instance ?? item.Element;
        foreach (var root in roots)
        {
            if (ReferenceEquals(element, root) || element.IsDescendantOf(root) || ReferenceEquals(item.Owner, root) || item.Owner.IsDescendantOf(root))
                return true;
        }
        return false;
    }

    private static Point ClosestOnSegment(Point a, Point b, Point p)
    {
        var ab = b - a;
        double len2 = ab.X * ab.X + ab.Y * ab.Y;
        if (len2 < 1e-12)
            return a;
        double t = Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / len2, 0, 1);
        return new Point(a.X + ab.X * t, a.Y + ab.Y * t);
    }

    private static readonly IPen SnapSourcePen = new Pen(new SolidColorBrush(Color.FromRgb(0x30, 0xE0, 0x50)), 1.5);
    private static readonly IBrush SnapSourceFill = new SolidColorBrush(Color.FromRgb(0x30, 0xE0, 0x50), 0.15);
    private static readonly IPen SnapCornerPen = new Pen(Brushes.Yellow, 2);
    private static readonly IBrush SnapCornerFill = new SolidColorBrush(Colors.Yellow, 0.15);
    private static readonly IPen SnapEdgePen = new Pen(Brushes.Cyan, 2);

    private void RenderSnap(DrawingContext context)
    {
        if (_snapKind == SnapKind.None || !_hasSnapSource)
            return;
        const double size = 7;
        if (_snapTarget is { } target)
        {
            if (_snapTargetCorner)
            {
                context.DrawLine(SnapCornerPen, _snapSource, target);
                context.DrawRectangle(SnapCornerFill, SnapCornerPen, new Rect(target.X - size, target.Y - size, size * 2, size * 2));
            }
            else
            {
                context.DrawLine(SnapEdgePen, _snapSource, target);
                context.DrawEllipse(Brushes.Cyan, null, target, 3.5, 3.5);
            }
        }
        context.DrawRectangle(SnapSourceFill, SnapSourcePen, new Rect(_snapSource.X - size, _snapSource.Y - size, size * 2, size * 2));
        context.DrawEllipse(null, SnapSourcePen, _snapSource, 3, 3);
    }
}
