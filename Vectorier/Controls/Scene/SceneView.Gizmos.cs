using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Vectorier.Models.Scene;
using Vectorier.ViewModels.Editor;

namespace Vectorier.Controls.Scene;

/// <summary>Part of a transform gizmo under the pointer.</summary>
internal enum GizmoHandle
{
    None,
    MoveX,
    MoveY,
    MoveFree,
    Rotate,
    ScaleX,
    ScaleY,
    ScaleUniform,
}

/// <summary>
/// Unity-like gizmos: Move arrows (X red, Y green, free square), a Rotate ring with arrows, Scale axes with
/// cubes and a center cube, and round corner handles for the Rect tool. They sit at the selection's center or at
/// the active element's pivot (toolbar), along the Scene's axes or the element's rotation (Global / Local).
/// The pivot setting only moves the gizmo: the data always keeps its top-left position.
/// </summary>
public partial class SceneView
{
    private const double GizmoLength = 76;
    private const double GizmoHitDistance = 7;
    private const double RotateRadius = 64;

    private static readonly Color XColor = Color.FromRgb(0xE5, 0x39, 0x35);
    private static readonly Color YColor = Color.FromRgb(0x43, 0xA0, 0x47);
    private static readonly Color FreeColor = Color.FromRgb(0x1E, 0x88, 0xE5);
    private static readonly Color HoverColor = Color.FromRgb(0xFF, 0xD5, 0x00);
    private static readonly Color RingColor = Color.FromRgb(0x29, 0x79, 0xFF);

    private GizmoHandle _gizmoHandle;
    private GizmoHandle _hoverHandle;
    private Vector _gizmoAx = new(1, 0);
    private Vector _gizmoAy = new(0, -1);

    /// <summary>Screen position and axes of the gizmo, or false when there is nothing to transform.</summary>
    private bool TryGetGizmo(SceneViewport viewport, out Point origin, out Vector ax, out Vector ay)
    {
        origin = default;
        ax = new Vector(1, 0);
        ay = new Vector(0, -1);
        if (_editor == null || _renderer == null || !_editor.HasSelection || _editor.PlacementType != null)
            return false;
        var targets = _editor.GetTopmostSelection();
        if (targets.Count == 0)
            return false;
        origin = GizmoOrigin(viewport, targets);

        if (_editor.IsLocalMode && LocalRotationSource(targets) is { } image)
        {
            double r = image.Rotation * Math.PI / 180;
            ax = new Vector(Math.Cos(r), Math.Sin(r));
            ay = new Vector(Math.Sin(r), -Math.Cos(r));
        }
        return true;
    }

    /// <summary>The image whose rotation gives the Local axes: the active element if it is a rotated image.</summary>
    private ImageElement? LocalRotationSource(List<SceneElement> targets)
    {
        if (_editor?.ActiveElement is ImageElement active)
            return active;
        return targets.Count == 1 ? targets[0] as ImageElement : null;
    }

    /// <summary>Center of the selection's bounds, or the active element's pivot (its position) in Pivot mode.</summary>
    private Point GizmoOrigin(SceneViewport viewport, List<SceneElement> targets)
    {
        if (_editor!.UsePivot)
        {
            var element = _editor.ActiveElement != null && targets.Any(t => ReferenceEquals(t, _editor.ActiveElement) || _editor.ActiveElement.IsDescendantOf(t))
                ? _editor.ActiveElement
                : targets[^1];
            var p = element.ParentWorldPosition;
            return new Point(p.X + element.X, p.Y + element.Y).Transform(viewport.LevelToScreen(_renderer!.GroupFor(element)));
        }
        return _renderer!.ScreenBounds(viewport, targets).Center;
    }

    private bool GizmoTool => Tool is EditorTool.Move or EditorTool.Rotate or EditorTool.Scale;

    private GizmoHandle HitGizmo(Point p)
    {
        if (!GizmoTool || !TryGetGizmo(Viewport, out var o, out var ax, out var ay))
            return GizmoHandle.None;
        switch (Tool)
        {
            case EditorTool.Move:
            {
                var sq = FreeSquareCenter(o, ax, ay);
                if (InAxisSquare(p, sq, ax, ay, 9)) return GizmoHandle.MoveFree;
                if (NearSegment(p, o, o + ax * GizmoLength) <= GizmoHitDistance) return GizmoHandle.MoveX;
                if (NearSegment(p, o, o + ay * GizmoLength) <= GizmoHitDistance) return GizmoHandle.MoveY;
                return GizmoHandle.None;
            }
            case EditorTool.Rotate:
            {
                double d = Distance(p, o);
                return Math.Abs(d - RotateRadius) <= GizmoHitDistance ? GizmoHandle.Rotate : GizmoHandle.None;
            }
            case EditorTool.Scale:
            {
                if (InAxisSquare(p, o, ax, ay, 8)) return GizmoHandle.ScaleUniform;
                if (InAxisSquare(p, o + ax * GizmoLength, ax, ay, 8) || NearSegment(p, o, o + ax * GizmoLength) <= GizmoHitDistance - 2) return GizmoHandle.ScaleX;
                if (InAxisSquare(p, o + ay * GizmoLength, ax, ay, 8) || NearSegment(p, o, o + ay * GizmoLength) <= GizmoHitDistance - 2) return GizmoHandle.ScaleY;
                return GizmoHandle.None;
            }
        }
        return GizmoHandle.None;
    }

    private static Point FreeSquareCenter(Point o, Vector ax, Vector ay) => o + ax * 16 + ay * 16;

    private static bool InAxisSquare(Point p, Point center, Vector ax, Vector ay, double half)
    {
        var d = p - center;
        return Math.Abs(d.X * ax.X + d.Y * ax.Y) <= half && Math.Abs(d.X * ay.X + d.Y * ay.Y) <= half;
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static double NearSegment(Point p, Point a, Point b)
    {
        var ab = b - a;
        double len2 = ab.X * ab.X + ab.Y * ab.Y;
        double t = len2 < 1e-9 ? 0 : Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / len2, 0, 1);
        return Distance(p, new Point(a.X + ab.X * t, a.Y + ab.Y * t));
    }

    /// <summary>Pointer pressed on a gizmo handle: starts the matching transform. Returns false when no handle was hit.</summary>
    private bool TryStartGizmoDrag(Point pos)
    {
        if (_editor == null || _renderer == null)
            return false;
        var handle = HitGizmo(pos);
        if (handle == GizmoHandle.None || !TryGetGizmo(Viewport, out var origin, out var ax, out var ay))
            return false;
        _gizmoHandle = handle;
        _gizmoAx = ax;
        _gizmoAy = ay;
        _pivotScreen = origin;
        var targets = _editor.GetTopmostSelection();
        var mode = handle switch
        {
            GizmoHandle.Rotate => InteractionMode.Rotate,
            GizmoHandle.ScaleX or GizmoHandle.ScaleY or GizmoHandle.ScaleUniform => InteractionMode.Scale,
            _ => InteractionMode.Move,
        };
        BeginTransform(mode, targets);
        return true;
    }

    /// <summary>Move delta constrained to the dragged arrow (projected on its axis).</summary>
    private Vector ConstrainMove(Vector delta) => _gizmoHandle switch
    {
        GizmoHandle.MoveX => _gizmoAx * (delta.X * _gizmoAx.X + delta.Y * _gizmoAx.Y),
        GizmoHandle.MoveY => _gizmoAy * (delta.X * _gizmoAy.X + delta.Y * _gizmoAy.Y),
        _ => delta,
    };

    /// <summary>Scale factors of a gizmo scale drag (along the handle's axis, or uniform for the center cube).</summary>
    private (double Sx, double Sy) GizmoScale(Point pos)
    {
        var d0 = _pressScreen - _pivotScreen;
        var d1 = pos - _pivotScreen;
        double Along(Vector v, Vector axis) => v.X * axis.X + v.Y * axis.Y;
        switch (_gizmoHandle)
        {
            case GizmoHandle.ScaleX:
            {
                double a0 = Along(d0, _gizmoAx);
                return (Math.Abs(a0) > 4 ? Along(d1, _gizmoAx) / a0 : 1 + (Along(d1, _gizmoAx) - a0) / 100, 1);
            }
            case GizmoHandle.ScaleY:
            {
                double a0 = Along(d0, _gizmoAy);
                return (1, Math.Abs(a0) > 4 ? Along(d1, _gizmoAy) / a0 : 1 + (Along(d1, _gizmoAy) - a0) / 100);
            }
            default:
            {
                // Center cube: right / up grows, left / down shrinks (like Unity).
                var drag = pos - _pressScreen;
                double s = Math.Max(0.01, 1 + (drag.X - drag.Y) / 100);
                return (s, s);
            }
        }
    }

    /// <summary>
    /// Local-axis scaling of a rotated image: its width and height vectors scale along their own directions and
    /// its position moves around the pivot in the same frame.
    /// </summary>
    private bool ScaleLocal(double sx, double sy)
    {
        if (_editor == null || !_editor.IsLocalMode || _targets.Count != 1 || _targets[0] is not ImageElement image || image.IsAxisAligned)
            return false;
        var pivot = LevelPointFor(image, _pivotScreen);
        double r = image.Rotation * Math.PI / 180;
        var ux = new Vector(Math.Cos(r), Math.Sin(r));
        var uy = new Vector(-Math.Sin(r), Math.Cos(r));
        var v = new Vector(image.X - pivot.X, image.Y - pivot.Y);
        double cx = v.X * ux.X + v.Y * ux.Y, cy = v.X * uy.X + v.Y * uy.Y;
        var nv = ux * (cx * sx) + uy * (cy * sy);
        var (a, b, c, d) = image.GetBasis();
        image.X = pivot.X + nv.X;
        image.Y = pivot.Y + nv.Y;
        image.SetBasis(a * sx, b * sx, c * sy, d * sy);
        return true;
    }

    // ================= DRAWING ================= //

    private void RenderGizmos(DrawingContext context, SceneViewport viewport)
    {
        if (_editor == null || _renderer == null || !_editor.HasSelection || _editor.PlacementType != null || IsModal)
            return;

        if (Tool == EditorTool.Rect)
        {
            var b = _renderer.ScreenBounds(viewport, _editor.GetTopmostSelection());
            if (b.Width <= 0 && b.Height <= 0)
                return;
            var pen = new Pen(new SolidColorBrush(FreeColor), 1.5);
            foreach (var c in new[] { b.TopLeft, b.TopRight, b.BottomRight, b.BottomLeft })
                context.DrawEllipse(Brushes.White, pen, c, 4.5, 4.5);
            if (TryGetGizmo(viewport, out var o, out _, out _))
            {
                context.DrawEllipse(null, pen, o, 5, 5);
                context.DrawEllipse(new SolidColorBrush(FreeColor), null, o, 1.5, 1.5);
            }
            return;
        }

        if (!GizmoTool || !TryGetGizmo(viewport, out var origin, out var ax, out var ay))
            return;
        var active = _mode is InteractionMode.Move or InteractionMode.Rotate or InteractionMode.Scale && _dragStarted ? _gizmoHandle : _hoverHandle;

        switch (Tool)
        {
            case EditorTool.Move:
                DrawArrow(context, origin, ax, active == GizmoHandle.MoveX ? HoverColor : XColor);
                DrawArrow(context, origin, ay, active == GizmoHandle.MoveY ? HoverColor : YColor);
                DrawAxisSquare(context, FreeSquareCenter(origin, ax, ay), ax, ay, 8,
                    new SolidColorBrush(active == GizmoHandle.MoveFree ? HoverColor : FreeColor, 0.45),
                    new Pen(new SolidColorBrush(active == GizmoHandle.MoveFree ? HoverColor : FreeColor), 1));
                break;

            case EditorTool.Rotate:
            {
                var color = active == GizmoHandle.Rotate ? HoverColor : RingColor;
                var pen = new Pen(new SolidColorBrush(color), 2);
                context.DrawEllipse(new SolidColorBrush(color, 0.06), pen, origin, RotateRadius, RotateRadius);
                // Curved arrows on the ring show the direction of rotation.
                foreach (double start in new[] { -0.35, Math.PI - 0.35 })
                    DrawRingArrow(context, origin, start, 0.7, color);
                context.DrawEllipse(new SolidColorBrush(color), null, origin, 2.5, 2.5);
                break;
            }

            case EditorTool.Scale:
                DrawScaleAxis(context, origin, ax, active == GizmoHandle.ScaleX ? HoverColor : XColor);
                DrawScaleAxis(context, origin, ay, active == GizmoHandle.ScaleY ? HoverColor : YColor);
                DrawAxisSquare(context, origin, ax, ay, 7,
                    new SolidColorBrush(active == GizmoHandle.ScaleUniform ? HoverColor : Color.FromRgb(0xB0, 0xB0, 0xB0)),
                    new Pen(Brushes.Black, 1));
                break;
        }
    }

    private static void DrawArrow(DrawingContext context, Point o, Vector axis, Color color)
    {
        var brush = new SolidColorBrush(color);
        var tip = o + axis * GizmoLength;
        var baseP = o + axis * (GizmoLength - 14);
        var n = new Vector(-axis.Y, axis.X);
        context.DrawLine(new Pen(brush, 2.5), o, baseP);
        var head = new StreamGeometry();
        using (var g = head.Open())
        {
            g.BeginFigure(tip, true);
            g.LineTo(baseP + n * 6);
            g.LineTo(baseP - n * 6);
            g.EndFigure(true);
        }
        context.DrawGeometry(brush, null, head);
    }

    private static void DrawScaleAxis(DrawingContext context, Point o, Vector axis, Color color)
    {
        var brush = new SolidColorBrush(color);
        var end = o + axis * GizmoLength;
        context.DrawLine(new Pen(brush, 2.5), o, end);
        DrawAxisSquare(context, end, axis, new Vector(-axis.Y, axis.X), 6, brush, new Pen(Brushes.Black, 1));
    }

    private static void DrawAxisSquare(DrawingContext context, Point c, Vector ax, Vector ay, double half, IBrush fill, IPen pen)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(c + ax * half + ay * half, true);
            ctx.LineTo(c - ax * half + ay * half);
            ctx.LineTo(c - ax * half - ay * half);
            ctx.LineTo(c + ax * half - ay * half);
            ctx.EndFigure(true);
        }
        context.DrawGeometry(fill, pen, g);
    }

    private static void DrawRingArrow(DrawingContext context, Point o, double start, double sweep, Color color)
    {
        var brush = new SolidColorBrush(color);
        double r = RotateRadius;
        Point At(double a) => new(o.X + Math.Cos(a) * r, o.Y + Math.Sin(a) * r);
        var arc = new StreamGeometry();
        using (var g = arc.Open())
        {
            g.BeginFigure(At(start), false);
            g.ArcTo(At(start + sweep), new Size(r, r), 0, false, SweepDirection.Clockwise);
            g.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(brush, 4), arc);
        // Arrow head at the end, along the tangent.
        double end = start + sweep;
        var tip = At(end + 0.12);
        var tangent = new Vector(-Math.Sin(end), Math.Cos(end));
        var normal = new Vector(Math.Cos(end), Math.Sin(end));
        var basePoint = At(end) - tangent * 2;
        var head = new StreamGeometry();
        using (var g = head.Open())
        {
            g.BeginFigure(tip, true);
            g.LineTo(basePoint + normal * 7);
            g.LineTo(basePoint - normal * 7);
            g.EndFigure(true);
        }
        context.DrawGeometry(brush, null, head);
    }

    // ================= CURSORS ================= //

    private static Cursor? _handCursor;
    private static Cursor? _zoomCursor;

    /// <summary>Open hand while panning with the right mouse button.</summary>
    private static Cursor HandCursor => _handCursor ??= MakeCursor("Icon.Pan") ?? new Cursor(StandardCursorType.SizeAll);

    /// <summary>Magnifying glass while zooming with Alt + right mouse button.</summary>
    private static Cursor ZoomCursor => _zoomCursor ??= MakeCursor("Icon.Search") ?? new Cursor(StandardCursorType.SizeNorthSouth);

    /// <summary>Renders an icon of the app (black with a white outline) into a 32×32 cursor.</summary>
    private static Cursor? MakeCursor(string iconKey)
    {
        try
        {
            if (Application.Current == null || !Application.Current.TryGetResource(iconKey, null, out var res) || res is not StreamGeometry icon)
                return null;
            var bitmap = new RenderTargetBitmap(new PixelSize(32, 32));
            using (var ctx = bitmap.CreateDrawingContext(true))
            {
                var b = icon.Bounds;
                double s = 24 / Math.Max(b.Width, b.Height);
                using (ctx.PushTransform(Matrix.CreateTranslation(-b.Center.X, -b.Center.Y) * Matrix.CreateScale(s, s) * Matrix.CreateTranslation(16, 16)))
                    ctx.DrawGeometry(Brushes.Black, new Pen(Brushes.White, 2.2 / s, lineJoin: PenLineJoin.Round), icon);
                using (ctx.PushTransform(Matrix.CreateTranslation(-b.Center.X, -b.Center.Y) * Matrix.CreateScale(s, s) * Matrix.CreateTranslation(16, 16)))
                    ctx.DrawGeometry(Brushes.Black, null, icon);
            }
            return new Cursor(bitmap, new PixelPoint(16, 16));
        }
        catch
        {
            return null;
        }
    }
}
