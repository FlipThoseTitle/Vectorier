using Avalonia;
using Avalonia.Media;

namespace Vectorier.Services.Assets;

/// <summary>
/// Silhouettes are unions of many overlapping shapes filled with the NonZero rule. Every figure must wind the same
/// way (like the circles, clockwise on screen), or overlapping parts with opposite winding cancel out and leave holes.
/// </summary>
public static class FillPolygon
{
    public static void Add(StreamGeometryContext ctx, params Point[] pts)
    {
        if (pts.Length < 3)
            return;
        double area = 0;
        for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
            area += pts[j].X * pts[i].Y - pts[i].X * pts[j].Y;
        if (System.Math.Abs(area) < 1e-12)
            return; // degenerate: nothing to fill
        if (area > 0)
        {
            ctx.BeginFigure(pts[0], true);
            for (int i = 1; i < pts.Length; i++)
                ctx.LineTo(pts[i]);
        }
        else
        {
            ctx.BeginFigure(pts[^1], true);
            for (int i = pts.Length - 2; i >= 0; i--)
                ctx.LineTo(pts[i]);
        }
        ctx.EndFigure(true);
    }
}
