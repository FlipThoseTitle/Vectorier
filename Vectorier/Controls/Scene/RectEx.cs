using System;
using Avalonia;

namespace Vectorier.Controls.Scene;

public static class RectEx
{
    /// <summary>Rectangle spanned by two corners in any order (Avalonia's Rect(Point, Point) keeps negative sizes).</summary>
    public static Rect Box(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
}
