#nullable disable warnings
using System;
using System.Numerics;

namespace Vectorier.Gameplay.Models;

/// <summary>
/// What the game's Raylib calls draw into, in the editor: one filled geometry (the model is a single colour).
/// <see cref="Transform"/> maps a node (level coordinates, Y down) to the canvas, like the game's
/// Sets.Current.Containers[1].TransformPoint, and <see cref="Scale"/> is its FrameScale.
/// </summary>
public sealed class ModelCanvas
{
    private readonly Avalonia.Media.StreamGeometryContext _ctx;
    private double _minX = double.MaxValue, _minY = double.MaxValue, _maxX = double.MinValue, _maxY = double.MinValue;

    public ModelCanvas(Avalonia.Media.StreamGeometryContext ctx, Func<Vector3, Avalonia.Point> transform, double scale = 1)
    {
        _ctx = ctx;
        Transform = transform;
        Scale = scale;
        _ctx.SetFillRule(Avalonia.Media.FillRule.NonZero);
    }

    public Func<Vector3, Avalonia.Point> Transform { get; }

    public double Scale { get; }

    /// <summary>What was drawn, in canvas units (empty when nothing was).</summary>
    public Avalonia.Rect Bounds => _minX <= _maxX ? new Avalonia.Rect(_minX, _minY, _maxX - _minX, _maxY - _minY) : default;

    public bool IsEmpty => _minX > _maxX;

    private void Grow(Avalonia.Point p, double r)
    {
        _minX = Math.Min(_minX, p.X - r); _minY = Math.Min(_minY, p.Y - r);
        _maxX = Math.Max(_maxX, p.X + r); _maxY = Math.Max(_maxY, p.Y + r);
    }

    /// <summary>Raylib.DrawCircleV.</summary>
    public void Circle(Avalonia.Point c, double r)
    {
        if (!(r > 0) || double.IsNaN(c.X) || double.IsNaN(c.Y))
            return;
        _ctx.BeginFigure(new Avalonia.Point(c.X + r, c.Y), true);
        _ctx.ArcTo(new Avalonia.Point(c.X - r, c.Y), new Avalonia.Size(r, r), 0, false, Avalonia.Media.SweepDirection.Clockwise);
        _ctx.ArcTo(new Avalonia.Point(c.X + r, c.Y), new Avalonia.Size(r, r), 0, false, Avalonia.Media.SweepDirection.Clockwise);
        _ctx.EndFigure(true);
        Grow(c, r);
    }

    /// <summary>Raylib.DrawLineEx: a line of a thickness with square-cut ends.</summary>
    public void Line(Avalonia.Point a, Avalonia.Point b, double thickness)
    {
        var d = b - a;
        double length = Math.Sqrt(d.X * d.X + d.Y * d.Y);
        if (length < 1e-9 || !(thickness > 0))
            return;
        var n = new Avalonia.Vector(-d.Y / length, d.X / length) * (thickness / 2);
        Polygon(a + n, b + n, b - n, a - n);
    }

    /// <summary>
    /// A filled polygon (Rlgl triangles with back-face culling off). Every figure winds the same way on screen,
    /// like the circles, so overlapping shapes add up instead of cancelling out under the NonZero rule.
    /// </summary>
    public void Polygon(params Avalonia.Point[] pts)
    {
        if (pts.Length < 3)
            return;
        double area = 0;
        for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
            area += pts[j].X * pts[i].Y - pts[i].X * pts[j].Y;
        if (Math.Abs(area) < 1e-12 || double.IsNaN(area))
            return; // degenerate: nothing to fill
        if (area > 0)
        {
            _ctx.BeginFigure(pts[0], true);
            for (int i = 1; i < pts.Length; i++)
                _ctx.LineTo(pts[i]);
        }
        else
        {
            _ctx.BeginFigure(pts[^1], true);
            for (int i = pts.Length - 2; i >= 0; i--)
                _ctx.LineTo(pts[i]);
        }
        _ctx.EndFigure(true);
        foreach (var p in pts)
            Grow(p, 0);
    }
}
