using System;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using Vectorier.Models.Scene;
using Vectorier.Services.Localization;
using Vectorier.ViewModels.Editor;

namespace Vectorier.Controls.Scene;

public partial class SceneView
{
    private Point LevelPointFor(SceneElement element, Point screen)
    {
        var group = Renderer!.GroupFor(element);
        var level = Viewport.ScreenToLevel(screen, group);
        var parent = element.ParentWorldPosition;
        return new Point(level.X - parent.X, level.Y - parent.Y);
    }

    private double PixelsToLevel(SceneElement element) => 1 / (Zoom * Renderer!.GroupFor(element).FrameScale);

    private void ApplyTransform(Point pos, KeyModifiers mods)
    {
        if (_editor == null || Renderer == null || _memento == null)
            return;
        _memento.Restore();
        bool ctrl = (mods & KeyModifiers.Control) != 0;
        bool shift = (mods & KeyModifiers.Shift) != 0;

        switch (_mode)
        {
            case InteractionMode.Move:
            case InteractionMode.ModalMove:
            case InteractionMode.Stamp:
                {
                    Vector delta = new(pos.X - _pressScreen.X, pos.Y - _pressScreen.Y);
                    if (_mode == InteractionMode.ModalMove && _axis == 'X') delta = new Vector(delta.X, 0);
                    else if (_mode == InteractionMode.ModalMove && _axis == 'Y') delta = new Vector(0, delta.Y);
                    else if (_mode == InteractionMode.Move && _gizmoHandle is GizmoHandle.MoveX or GizmoHandle.MoveY) delta = ConstrainMove(delta);
                    else if (shift && _mode == InteractionMode.Move) delta = Math.Abs(delta.X) >= Math.Abs(delta.Y) ? new Vector(delta.X, 0) : new Vector(0, delta.Y);
                    foreach (var t in _targets)
                    {
                        double k = PixelsToLevel(t);
                        var (sx, sy) = _memento.StartPosition(t);
                        t.X = sx + delta.X * k;
                        t.Y = sy + delta.Y * k;
                        if (t.IntegerTransform)
                        {
                            t.X = Math.Round(t.X);
                            t.Y = Math.Round(t.Y);
                        }
                    }
                    break;
                }

            case InteractionMode.Rotate:
            case InteractionMode.ModalRotate:
                {
                    double a0 = Math.Atan2(_pressScreen.Y - _pivotScreen.Y, _pressScreen.X - _pivotScreen.X);
                    double a1 = Math.Atan2(pos.Y - _pivotScreen.Y, pos.X - _pivotScreen.X);
                    double angle = a1 - a0;
                    if (ctrl)
                    {
                        double step = Math.PI / 12;
                        angle = Math.Round(angle / step) * step;
                    }
                    RotateTargets(angle);
                    _editor.StatusText = Loc.F("Rotate {0:0.##}°", -angle * 180 / Math.PI);
                    break;
                }

            case InteractionMode.Scale:
            case InteractionMode.ModalScale:
                {
                    var d0 = _pressScreen - _pivotScreen;
                    var d1 = pos - _pivotScreen;
                    double len0 = Math.Max(1, Math.Sqrt(d0.X * d0.X + d0.Y * d0.Y));
                    double len1 = Math.Sqrt(d1.X * d1.X + d1.Y * d1.Y);
                    double s = len1 / len0;
                    double sx = s, sy = s;
                    if (_mode == InteractionMode.Scale && _gizmoHandle is GizmoHandle.ScaleX or GizmoHandle.ScaleY or GizmoHandle.ScaleUniform)
                    {
                        (sx, sy) = GizmoScale(pos);
                        if (!ScaleLocal(sx, sy))
                            ScaleTargets(_pivotScreen, sx, sy, ctrl);
                        _editor.StatusText = Loc.F("Scale {0:0.###} × {1:0.###}", sx, sy);
                        break;
                    }
                    if (_axis == 'X') sy = 1;
                    else if (_axis == 'Y') sx = 1;
                    else if (shift)
                    {
                        sx = Math.Abs(d0.X) > 2 ? Math.Abs(d1.X / d0.X) : 1;
                        sy = Math.Abs(d0.Y) > 2 ? Math.Abs(d1.Y / d0.Y) : 1;
                    }
                    ScaleTargets(_pivotScreen, sx, sy, ctrl);
                    _editor.StatusText = Loc.F("Scale {0:0.###} × {1:0.###}", sx, sy);
                    break;
                }

            case InteractionMode.RectTransform:
                ApplyRect(pos, ctrl, shift);
                break;

            case InteractionMode.TrapezoidBase:
                {
                    if (_targets.FirstOrDefault() is TrapezoidElement trapezoid)
                    {
                        double slope = trapezoid.SlopeHeight;
                        double dy = (pos.Y - _pressScreen.Y) * PixelsToLevel(trapezoid);
                        trapezoid.BaseHeight = Math.Max(1, Math.Round(trapezoid.BaseHeight + dy));
                        trapezoid.Height = trapezoid.BaseHeight + slope;
                    }
                    break;
                }
        }
        InvalidateVisual();
    }

    private void RotateTargets(double angle)
    {
        _editor?.Dynamics.NoteTransform(DynamicTransformKind.Rotate);
        bool single = _targets.Count == 1;
        foreach (var t in _targets)
        {
            var pivot = LevelPointFor(t, _pivotScreen);
            if (t.CanRotate || t is ObjectElement)
            {
                t.RotateAround(pivot, angle);
                continue;
            }
            if (single)
                continue; // Platforms, Trapezoids, Models... cannot rotate.

            // In a multi-selection they orbit the pivot by their center.
            var b = t.GetLocalBounds();
            var c = b.Center;
            double cos = Math.Cos(angle), sin = Math.Sin(angle);
            double dx = c.X - pivot.X, dy = c.Y - pivot.Y;
            var nc = new Point(pivot.X + dx * cos - dy * sin, pivot.Y + dx * sin + dy * cos);
            t.X += nc.X - c.X;
            t.Y += nc.Y - c.Y;
            t.ApplyIntegerLock();
        }
    }

    private void ScaleTargets(Point pivotScreen, double sx, double sy, bool free)
    {
        _editor?.Dynamics.NoteTransform(DynamicTransformKind.Scale);
        foreach (var t in _targets)
        {
            if (t is ModelElement && _targets.Count == 1)
                continue; // Models can only be moved.
            var pivot = LevelPointFor(t, pivotScreen);
            double tx = sx, ty = sy;
            if (t is TrapezoidElement && !free)
                ty = tx = Math.Abs(sx - 1) > Math.Abs(sy - 1) ? sx : sy; // keep the 26.565° slope
            t.ScaleAround(pivot, tx, ty);
            if (t.IntegerTransform)
            {
                t.ApplyIntegerLock();
                if (t is TrapezoidElement trapezoid && !free)
                    trapezoid.ApplySlopeAngle();
            }
        }
    }

    private void ApplyRect(Point pos, bool ctrl, bool shift)
    {
        if ((_rectHandle & RectHandle.Move) != 0)
        {
            Vector delta = new(pos.X - _pressScreen.X, pos.Y - _pressScreen.Y);
            if (shift) delta = Math.Abs(delta.X) >= Math.Abs(delta.Y) ? new Vector(delta.X, 0) : new Vector(0, delta.Y);
            foreach (var t in _targets)
            {
                double k = PixelsToLevel(t);
                var (sx0, sy0) = _memento!.StartPosition(t);
                t.X = sx0 + delta.X * k;
                t.Y = sy0 + delta.Y * k;
                if (t.IntegerTransform)
                {
                    t.X = Math.Round(t.X);
                    t.Y = Math.Round(t.Y);
                }
            }
            return;
        }

        if ((_rectHandle & RectHandle.Rotate) != 0)
        {
            double a0 = Math.Atan2(_pressScreen.Y - _pivotScreen.Y, _pressScreen.X - _pivotScreen.X);
            double a1 = Math.Atan2(pos.Y - _pivotScreen.Y, pos.X - _pivotScreen.X);
            double angle = a1 - a0;
            if (ctrl)
                angle = Math.Round(angle / (Math.PI / 12)) * (Math.PI / 12);
            RotateTargets(angle);
            return;
        }

        // Resize: the opposite edge / corner stays fixed.
        var r = _rectStart;
        double left = r.Left, right = r.Right, top = r.Top, bottom = r.Bottom;
        if ((_rectHandle & RectHandle.Left) != 0) left = Math.Min(pos.X, right - 1);
        if ((_rectHandle & RectHandle.Right) != 0) right = Math.Max(pos.X, left + 1);
        if ((_rectHandle & RectHandle.Top) != 0) top = Math.Min(pos.Y, bottom - 1);
        if ((_rectHandle & RectHandle.Bottom) != 0) bottom = Math.Max(pos.Y, top + 1);

        double sx = r.Width > 0.5 ? (right - left) / r.Width : 1;
        double sy = r.Height > 0.5 ? (bottom - top) / r.Height : 1;
        bool horizontal = (_rectHandle & (RectHandle.Left | RectHandle.Right)) != 0;
        bool vertical = (_rectHandle & (RectHandle.Top | RectHandle.Bottom)) != 0;
        if (!horizontal) sx = 1;
        if (!vertical) sy = 1;
        if (shift && horizontal && vertical)
            sx = sy = Math.Max(sx, sy);

        var anchor = new Point(
            (_rectHandle & RectHandle.Left) != 0 ? r.Right : r.Left,
            (_rectHandle & RectHandle.Top) != 0 ? r.Bottom : r.Top);

        // Shift on a side handle: uniform scale too, the other axis growing evenly around the center.
        if (shift && horizontal != vertical)
        {
            double k = horizontal ? sx : sy;
            sx = sy = k;
            anchor = horizontal ? new Point(anchor.X, r.Center.Y) : new Point(r.Center.X, anchor.Y);
        }
        ScaleTargets(anchor, sx, sy, ctrl);
    }

    private RectHandle RectZone(Point pos)
    {
        if (_editor == null || Renderer == null)
            return RectHandle.None;
        var b = Renderer.ScreenBounds(Viewport, _editor.GetTopmostSelection());
        if (b.Width <= 0 && b.Height <= 0)
            return RectHandle.None;

        const double edge = 6, corner = 9, rotate = 24;
        bool nearL = Math.Abs(pos.X - b.Left) <= edge, nearR = Math.Abs(pos.X - b.Right) <= edge;
        bool nearT = Math.Abs(pos.Y - b.Top) <= edge, nearB = Math.Abs(pos.Y - b.Bottom) <= edge;
        bool withinX = pos.X >= b.Left - edge && pos.X <= b.Right + edge;
        bool withinY = pos.Y >= b.Top - edge && pos.Y <= b.Bottom + edge;

        foreach (var (cp, h) in new[] { (b.TopLeft, RectHandle.Left | RectHandle.Top), (b.TopRight, RectHandle.Right | RectHandle.Top), (b.BottomLeft, RectHandle.Left | RectHandle.Bottom), (b.BottomRight, RectHandle.Right | RectHandle.Bottom) })
        {
            double dist = Math.Sqrt((pos.X - cp.X) * (pos.X - cp.X) + (pos.Y - cp.Y) * (pos.Y - cp.Y));
            if (dist <= corner)
                return h;
            if (dist <= rotate && !b.Contains(pos))
                return RectHandle.Rotate;
        }

        var result = RectHandle.None;
        if (nearL && withinY) result |= RectHandle.Left;
        else if (nearR && withinY) result |= RectHandle.Right;
        if (nearT && withinX) result |= RectHandle.Top;
        else if (nearB && withinX) result |= RectHandle.Bottom;
        if (result != RectHandle.None)
            return result;
        return b.Contains(pos) ? RectHandle.Move : RectHandle.None;
    }

    /// <summary>Small handle at the bottom of a single selected trapezoid that changes its base height.</summary>
    private Rect? TrapezoidHandle(SceneViewport viewport)
    {
        if (_editor == null || Renderer == null || _editor.Selection.Count != 1 || _editor.Selection[0] is not TrapezoidElement trapezoid)
            return null;
        var pts = Renderer.ScreenCorners(viewport, trapezoid);
        var bottom = new Point((pts[2].X + pts[3].X) / 2, (pts[2].Y + pts[3].Y) / 2);
        return new Rect(bottom.X - 4, bottom.Y - 4, 8, 8);
    }

    private void UpdateCursor(Point pos)
    {
        if (_editor == null)
            return;
        if (SpacePanHeld)
        {
            Cursor = HandCursor;
            return;
        }
        StandardCursorType type = StandardCursorType.Arrow;
        if (_editor.PlacementType != null || Tool == EditorTool.Brush)
            type = StandardCursorType.Cross;
        else if (_hoverHandle != GizmoHandle.None)
            type = StandardCursorType.Hand;
        else if (TrapezoidHandle(Viewport) is { } h && h.Inflate(3).Contains(pos))
            type = StandardCursorType.SizeNorthSouth;
        else if (Tool == EditorTool.Rect && _editor.HasSelection)
        {
            var zone = RectZone(pos);
            if (zone == RectHandle.Rotate)
            {
                Cursor = RotateCursor;
                return;
            }
            type = zone switch
            {
                RectHandle.Move => StandardCursorType.SizeAll,
                RectHandle.Left or RectHandle.Right => StandardCursorType.SizeWestEast,
                RectHandle.Top or RectHandle.Bottom => StandardCursorType.SizeNorthSouth,
                RectHandle.Left | RectHandle.Top or RectHandle.Right | RectHandle.Bottom => StandardCursorType.TopLeftCorner,
                RectHandle.Right | RectHandle.Top or RectHandle.Left | RectHandle.Bottom => StandardCursorType.TopRightCorner,
                _ => StandardCursorType.Arrow,
            };
        }
        Cursor = type == StandardCursorType.Arrow ? null : new Cursor(type);
    }
}
