using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Vectorier.Models.Scene;
using Vectorier.Services.Assets;
using Vectorier.ViewModels.Editor;

namespace Vectorier.Controls.Scene;

/// <summary>Brush tool: painting strokes and shapes, the canvas above the Scene and the brush outline.</summary>
public partial class SceneView
{
    private static readonly Pen CanvasPen = new(new SolidColorBrush(Color.FromArgb(0xC0, 0x30, 0x90, 0xFF)), 1, new DashStyle(new double[] { 4, 3 }, 0));
    private static readonly Pen ClipPen = new(new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0x8C, 0x00)), 1.5);
    private static readonly Pen BrushOuterPen = new(new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0)), 1.5);
    private static readonly Pen BrushInnerPen = new(new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)), 1);
    private DispatcherTimer? _sprayTimer;

    // Shape tools: the shape being drawn, in level space of the canvas's layer. Rectangle / Circle: the press
    // point and _shapeEnd; Lasso: the dragged path; Polyline: the points placed so far (no drag).
    private readonly List<Point> _shape = new();
    private Point _shapeEnd;
    private Point _lassoLastScreen;
    private const double PolylineCloseDistance = 8;

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
        if (paint.Kind.IsShape())
        {
            PressShape(e, pos, paint);
            return true;
        }
        e.Pointer.Capture(this);
        _mode = InteractionMode.Paint;
        paint.BeginStroke(PaintLevel(pos));
        if (paint.Kind == BrushKind.Airbrush)
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
        else if (_mode == InteractionMode.PaintShape)
            DragShape(pos);
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

    // ================= SHAPES ================= //

    private void PressShape(PointerPressedEventArgs e, Point pos, PaintViewModel paint)
    {
        var level = PaintLevel(pos);
        if (paint.Kind != BrushKind.Polyline)
        {
            e.Pointer.Capture(this);
            _mode = InteractionMode.PaintShape;
            _shape.Clear();
            _shape.Add(level);
            _shapeEnd = level;
            _lassoLastScreen = pos;
            return;
        }

        // Polyline: each click places a point; clicking the first point again (or a double click) closes and fills.
        if (!IsPlacingPolyline)
            _shape.Clear();
        _mode = InteractionMode.None;
        if (_shape.Count >= 3 && (e.ClickCount >= 2 || Distance(pos, ShapeToScreen(paint, _shape[0])) <= PolylineCloseDistance))
        {
            FinishPolyline();
            return;
        }
        if (e.ClickCount < 2) // the double click's first click already placed this point
            _shape.Add(level);
        InvalidateVisual();
    }

    private void DragShape(Point pos)
    {
        var paint = _editor!.Paint;
        if (paint.Kind == BrushKind.Lasso)
        {
            if (Distance(pos, _lassoLastScreen) < 2)
                return;
            _lassoLastScreen = pos;
            _shape.Add(PaintLevel(pos));
        }
        else
        {
            _shapeEnd = PaintLevel(pos);
        }
        InvalidateVisual();
    }

    /// <summary>Button released: the Rectangle, Circle or Lasso is filled.</summary>
    private void ReleaseShape()
    {
        var paint = _editor!.Paint;
        _mode = InteractionMode.None;
        var polygon = paint.Kind == BrushKind.Lasso ? new List<Point>(_shape) : BoxShape(paint.Kind);
        _shape.Clear();
        if (polygon.Count >= 3 && HasArea(paint, polygon))
            paint.FillShape(polygon);
        InvalidateVisual();
    }

    /// <summary>The drag was taken away (pointer capture lost): the shape is dropped.</summary>
    private void CancelShapeDrag()
    {
        if (_mode != InteractionMode.PaintShape)
            return;
        _mode = InteractionMode.None;
        _shape.Clear();
        InvalidateVisual();
    }

    /// <summary>Points are being placed with the Polyline (it is dropped as soon as anything else is picked).</summary>
    private bool IsPlacingPolyline
    {
        get
        {
            if (_shape.Count == 0 || _mode == InteractionMode.PaintShape)
                return false;
            if (_editor is { Paint: { IsActive: true, Kind: BrushKind.Polyline } } && Tool == EditorTool.Brush)
                return true;
            _shape.Clear();
            return false;
        }
    }

    /// <summary>Enter (or clicking the first point): closes the Polyline and fills it. False when none is being drawn.</summary>
    public bool FinishPolyline()
    {
        if (!IsPlacingPolyline)
            return false;
        var polygon = new List<Point>(_shape);
        _shape.Clear();
        if (polygon.Count >= 3 && HasArea(_editor!.Paint, polygon))
            _editor.Paint.FillShape(polygon);
        InvalidateVisual();
        return true;
    }

    /// <summary>Esc: drops the shape being drawn. False when there is none.</summary>
    private bool CancelShape()
    {
        if (_mode != InteractionMode.PaintShape && !IsPlacingPolyline)
            return false;
        _mode = InteractionMode.None;
        _shape.Clear();
        InvalidateVisual();
        return true;
    }

    /// <summary>Rectangle / Circle: the shape inside the box from the press point to the pointer (level space).</summary>
    private List<Point> BoxShape(BrushKind kind)
    {
        var box = new Rect(_shape[0], _shapeEnd).Normalize();
        if (kind == BrushKind.Rectangle)
            return new List<Point> { box.TopLeft, box.TopRight, box.BottomRight, box.BottomLeft };
        const int segments = 256;
        var c = box.Center;
        var ellipse = new List<Point>(segments);
        for (int i = 0; i < segments; i++)
        {
            double a = i * 2 * Math.PI / segments;
            ellipse.Add(new Point(c.X + Math.Cos(a) * box.Width / 2, c.Y + Math.Sin(a) * box.Height / 2));
        }
        return ellipse;
    }

    /// <summary>A shape thinner than a screen pixel (a click without a drag) fills nothing.</summary>
    private bool HasArea(PaintViewModel paint, List<Point> polygon)
    {
        double area = 0;
        var m = Viewport.LevelToScreen(PaintGroup(paint));
        for (int i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i].Transform(m);
            var b = polygon[(i + 1) % polygon.Count].Transform(m);
            area += a.X * b.Y - b.X * a.Y;
        }
        return Math.Abs(area) / 2 >= 1;
    }

    private Point ShapeToScreen(PaintViewModel paint, Point level) => level.Transform(Viewport.LevelToScreen(PaintGroup(paint)));

    /// <summary>The shape being drawn: filled with the brush color (outline only when erasing), and its points.</summary>
    private void RenderShapePreview(DrawingContext context, SceneViewport viewport, PaintViewModel paint)
    {
        bool polyline = IsPlacingPolyline;
        if (_mode != InteractionMode.PaintShape && !polyline)
            return;
        var outline = _mode == InteractionMode.PaintShape && paint.Kind != BrushKind.Lasso ? BoxShape(paint.Kind) : new List<Point>(_shape);
        var toScreen = viewport.LevelToScreen(PaintGroup(paint));
        for (int i = 0; i < outline.Count; i++)
            outline[i] = outline[i].Transform(toScreen);
        bool closing = polyline && outline.Count >= 3 && Distance(_lastPointer, outline[0]) <= PolylineCloseDistance;
        if (polyline && IsPointerInside && !closing)
            outline.Add(_lastPointer); // the next point follows the pointer

        if (outline.Count >= 2)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.SetFillRule(FillRule.NonZero);
                ctx.BeginFigure(outline[0], true);
                for (int i = 1; i < outline.Count; i++)
                    ctx.LineTo(outline[i]);
                ctx.EndFigure(!polyline || closing);
            }
            IBrush? fill = paint.Erase ? null : new SolidColorBrush(paint.Color, Math.Clamp(paint.Opacity / 100, 0, 1) * 0.5);
            context.DrawGeometry(fill, BrushOuterPen, geometry);
            context.DrawGeometry(null, BrushInnerPen, geometry);
        }
        if (polyline)
        {
            for (int i = 0; i < _shape.Count; i++)
            {
                var p = outline[i];
                double half = i == 0 && closing ? 5 : 3;
                context.DrawRectangle(Brushes.White, BrushOuterPen, new Rect(p.X - half, p.Y - half, half * 2, half * 2));
            }
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

    /// <summary>Brush outline at the pointer (its size in the canvas's layer), or the shape being drawn.</summary>
    private void RenderBrushCursor(DrawingContext context, SceneViewport viewport)
    {
        if (_editor == null || Tool != EditorTool.Brush)
            return;
        var paint = _editor.Paint;
        if (paint.Kind.IsShape())
        {
            RenderShapePreview(context, viewport, paint);
            return;
        }
        if (!IsPointerInside)
            return;
        var group = PaintGroup(paint);
        double radius = Math.Max(1.5, paint.Size / 2 * viewport.Zoom * group.FrameScale);
        context.DrawEllipse(null, BrushOuterPen, _lastPointer, radius + 0.5, radius + 0.5);
        context.DrawEllipse(null, BrushInnerPen, _lastPointer, radius, radius);
        if (paint.Kind != BrushKind.Hard)
            context.DrawEllipse(null, BrushInnerPen, _lastPointer, radius * 0.5, radius * 0.5);
    }
}
