using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Vectorier.Models.Scene;
using Vectorier.ViewModels.Editor;

namespace Vectorier.Controls.Scene;

/// <summary>Brush tool: painting strokes, the canvas above the Scene and the brush outline.</summary>
public partial class SceneView
{
    private static readonly Pen CanvasPen = new(new SolidColorBrush(Color.FromArgb(0xC0, 0x30, 0x90, 0xFF)), 1, new DashStyle(new double[] { 4, 3 }, 0));
    private static readonly Pen ClipPen = new(new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0x8C, 0x00)), 1.5);
    private static readonly Pen BrushOuterPen = new(new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0)), 1.5);
    private static readonly Pen BrushInnerPen = new(new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)), 1);
    private DispatcherTimer? _sprayTimer;

    private LayerGroup PaintGroup(PaintViewModel paint)
        => new(paint.IsActive ? paint.LayerFactor : Layers.GetFactor(Layers.Default), -1);

    private Point PaintLevel(Point screen)
    {
        var paint = _editor!.Paint;
        return Viewport.ScreenToLevel(screen, PaintGroup(paint));
    }

    /// <summary>Left press with the Brush: starts a new image or a stroke.</summary>
    private async System.Threading.Tasks.Task<bool> TryPaintPressAsync(PointerPressedEventArgs e, Point pos)
    {
        if (_editor == null || Tool != EditorTool.Brush)
            return false;
        e.Handled = true;
        var paint = _editor.Paint;
        if (!paint.IsActive)
        {
            _mode = InteractionMode.None;
            await paint.StartNewAsync(PaintLevel(pos));
            InvalidateVisual();
            return true;
        }
        e.Pointer.Capture(this);
        _mode = InteractionMode.Paint;
        paint.BeginStroke(PaintLevel(pos));
        if (paint.Kind == Services.Assets.BrushKind.Airbrush)
        {
            _sprayTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Input, (_, _) => _editor?.Paint.Spray());
            _sprayTimer.Start();
        }
        return true;
    }

    private void PaintMoved(Point pos)
    {
        if (_editor == null)
            return;
        if (_mode == InteractionMode.Paint)
            _editor.Paint.StrokeTo(PaintLevel(pos));
        else if (Tool == EditorTool.Brush)
            InvalidateVisual(); // the brush outline follows the pointer
    }

    private void EndPaintStroke()
    {
        _sprayTimer?.Stop();
        if (_mode == InteractionMode.Paint)
        {
            _mode = InteractionMode.None;
            _editor?.Paint.EndStroke();
        }
    }

    /// <summary>The canvas being painted, above every layer.</summary>
    private void RenderPaintCanvas(DrawingContext context, SceneViewport viewport)
    {
        var paint = _editor?.Paint;
        if (paint?.Canvas is not { } canvas)
            return;
        var toScreen = canvas.CanvasToLevel * viewport.LevelToScreen(PaintGroup(paint));
        var bitmap = canvas.GetBitmap();
        using (context.PushTransform(toScreen))
            context.DrawImage(bitmap, new Rect(0, 0, canvas.Width, canvas.Height), new Rect(0, 0, canvas.Width, canvas.Height));

        // Outline of what will be saved: exactly what has been painted (plus the edited image's own area), so it
        // grows with the paint and shrinks back on undo. When clipping, the image's own outline too.
        if (canvas.PaintedBounds is { } painted)
            DrawQuad(context, CanvasPen, new Rect(painted.X, painted.Y, painted.Width, painted.Height), toScreen);
        if (canvas.ClipToOriginal || (paint.ClipToBounds && paint.CanClip))
        {
            var o = canvas.Original;
            DrawQuad(context, ClipPen, new Rect(o.X, o.Y, o.Width, o.Height), toScreen);
        }
    }

    private static void DrawQuad(DrawingContext context, Pen pen, Rect r, Matrix m)
    {
        var a = r.TopLeft.Transform(m);
        var b = r.TopRight.Transform(m);
        var c = r.BottomRight.Transform(m);
        var d = r.BottomLeft.Transform(m);
        context.DrawLine(pen, a, b);
        context.DrawLine(pen, b, c);
        context.DrawLine(pen, c, d);
        context.DrawLine(pen, d, a);
    }

    /// <summary>Brush outline at the pointer (its size in the canvas's layer).</summary>
    private void RenderBrushCursor(DrawingContext context, SceneViewport viewport)
    {
        if (_editor == null || Tool != EditorTool.Brush || !IsPointerInside)
            return;
        var paint = _editor.Paint;
        var group = PaintGroup(paint);
        double radius = Math.Max(1.5, paint.Size / 2 * viewport.Zoom * group.FrameScale);
        context.DrawEllipse(null, BrushOuterPen, _lastPointer, radius + 0.5, radius + 0.5);
        context.DrawEllipse(null, BrushInnerPen, _lastPointer, radius, radius);
        if (paint.Kind != Services.Assets.BrushKind.Hard)
            context.DrawEllipse(null, BrushInnerPen, _lastPointer, radius * 0.5, radius * 0.5);
    }
}
