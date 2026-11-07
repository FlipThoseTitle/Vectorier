using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Vectorier.Models.Scene;

namespace Vectorier.Controls.Scene;

/// <summary>
/// A Moving Animation's Direction, set in the Scene: an arrow from the animation's center, drawn while it is the
/// only element selected. Dragging its tip sets the direction (the file keeps "X|Y", units per frame); the arrow
/// is <see cref="DirectionArrowScale"/> screen units long per unit of direction (the same at any zoom).
/// </summary>
public partial class SceneView
{
    private const double DirectionArrowScale = 15;
    private const double DirectionHitRadius = 8;

    /// <summary>With no direction, the handle waits this far to the right of the center (screen units).</summary>
    private const double DirectionIdleOffset = 28;

    private static readonly Color DirectionColor = Color.FromRgb(0xFF, 0x8F, 0x00);
    private static readonly Pen DirectionPen = new(new SolidColorBrush(DirectionColor), 2);
    private static readonly Pen DirectionIdlePen = new(new SolidColorBrush(Color.FromArgb(0xA0, 0xFF, 0x8F, 0x00)), 1.5, new DashStyle(new double[] { 3, 3 }, 0));

    private AnimationElement? _directionElement;

    /// <summary>The selected Moving Animation (alone in the selection), or null.</summary>
    private AnimationElement? DirectionTarget()
    {
        if (_editor == null || _renderer == null || IsGameView || _editor.PlacementType != null || _editor.Paint.IsActive)
            return null;
        if (_editor.Selection.Count != 1 || _editor.Selection[0] is not AnimationElement { AnimationType: 1 } animation || animation.IsLockedInScene)
            return null;
        return animation;
    }

    /// <summary>The arrow on the screen: its start (the animation's center), its tip, and whether it has a direction.</summary>
    private (Point Center, Point Tip, bool HasDirection)? DirectionArrow(SceneViewport viewport)
    {
        if (DirectionTarget() is not { } animation)
            return null;
        var m = viewport.LevelToScreen(_renderer!.GroupFor(animation));
        var origin = animation.ParentWorldPosition;
        var bounds = animation.GetLocalBounds();
        var center = new Point(origin.X + bounds.Center.X, origin.Y + bounds.Center.Y);
        var (dx, dy) = animation.DirectionVector;
        var screenCenter = center.Transform(m);
        var tip = screenCenter + new Vector(dx, dy) * DirectionArrowScale;
        bool has = ((Vector)(tip - screenCenter)).Length >= DirectionHitRadius * 1.5;
        return (screenCenter, has ? tip : screenCenter + new Vector(DirectionIdleOffset, 0), has);
    }

    private void RenderAnimationDirection(DrawingContext context, SceneViewport viewport)
    {
        if (DirectionArrow(viewport) is not { } arrow)
            return;
        bool active = _mode == InteractionMode.AnimationDirection;
        if (arrow.HasDirection)
        {
            context.DrawLine(DirectionPen, arrow.Center, arrow.Tip);
            var dir = (Vector)(arrow.Tip - arrow.Center);
            dir /= dir.Length;
            var normal = new Vector(-dir.Y, dir.X);
            var back = arrow.Tip - dir * 12;
            var head = new StreamGeometry();
            using (var g = head.Open())
            {
                g.BeginFigure(arrow.Tip, true);
                g.LineTo(back + normal * 6);
                g.LineTo(back - normal * 6);
                g.EndFigure(true);
            }
            context.DrawGeometry(new SolidColorBrush(DirectionColor), null, head);
        }
        else
        {
            context.DrawLine(DirectionIdlePen, arrow.Center, arrow.Tip);
        }
        context.DrawEllipse(active ? new SolidColorBrush(HoverColor) : Brushes.White, DirectionPen, arrow.Tip, 5, 5);
    }

    /// <summary>A press on the arrow's tip starts dragging the direction.</summary>
    private bool TryStartAnimationDirection(PointerPressedEventArgs e, Point pos)
    {
        if (DirectionArrow(Viewport) is not { } arrow || ((Vector)(pos - arrow.Tip)).Length > DirectionHitRadius)
            return false;
        _directionElement = DirectionTarget();
        _mode = InteractionMode.AnimationDirection;
        _editor!.BeginGesture(); // the whole drag is one undo step
        e.Pointer.Capture(this);
        e.Handled = true;
        return true;
    }

    private void DragAnimationDirection(Point pos)
    {
        if (_directionElement is not { } animation || _renderer == null)
            return;
        var m = Viewport.LevelToScreen(_renderer.GroupFor(animation));
        var origin = animation.ParentWorldPosition;
        var bounds = animation.GetLocalBounds();
        var center = new Point(origin.X + bounds.Center.X, origin.Y + bounds.Center.Y).Transform(m);
        double dx = (pos.X - center.X) / DirectionArrowScale;
        double dy = (pos.Y - center.Y) / DirectionArrowScale;
        // Two decimals.
        dx = Math.Round(dx, 2);
        dy = Math.Round(dy, 2);
        animation.DirectionVector = (dx, dy);
        InvalidateVisual();
    }

    private void EndAnimationDirection()
    {
        _directionElement = null;
        _editor?.EndGesture();
    }
}
