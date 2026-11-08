#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Numerics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AMatrix = Avalonia.Matrix;
using APoint = Avalonia.Point;

namespace Vectorier.Gameplay.Rl;

/// <summary>Raylib's rlgl immediate mode: the game draws textured quads (affine images) and triangles.</summary>
public static class Rlgl
{
    private static DrawMode _mode;
    private static uint _texture;
    private static Color _color = Color.White;
    private static readonly List<(Vector2 Position, Vector2 Uv, Color Color)> _vertices = new();
    private static Vector2 _uv;

    public static void SetTexture(uint id) => _texture = id;
    public static void Begin(DrawMode mode)
    {
        _mode = mode;
        _vertices.Clear();
    }
    public static void Color4ub(byte r, byte g, byte b, byte a) => _color = new Color(r, g, b, a);
    public static void TexCoord2f(float u, float v) => _uv = new Vector2(u, v);
    public static void Vertex2f(float x, float y) => _vertices.Add((new Vector2(x, y), _uv, _color));

    public static void End()
    {
        var ctx = Raylib.Context;
        if (ctx == null)
        {
            _vertices.Clear();
            return;
        }
        int per = _mode == DrawMode.Quads ? 4 : _mode == DrawMode.Triangles ? 3 : 2;
        for (int i = 0; i + per <= _vertices.Count; i += per)
        {
            if (_mode == DrawMode.Quads && Raylib.TextureImage(_texture) is { } image)
                DrawTexturedQuad(ctx, image, i);
            else if (_mode != DrawMode.Lines)
                FillPolygon(ctx, i, per);
            else
                Raylib.DrawLineV(_vertices[i].Position, _vertices[i + 1].Position, _vertices[i].Color);
        }
        _vertices.Clear();
    }

    /// <summary>
    /// A textured quad from the game's image drawing is a parallelogram (an affine transform of the texture):
    /// the image is drawn under the matrix mapping its texture corners onto the quad's first three corners.
    /// </summary>
    private static void DrawTexturedQuad(DrawingContext ctx, IImage image, int i)
    {
        var (p0, t0, c0) = _vertices[i];
        var (p1, t1, _) = _vertices[i + 1];
        var (p3, t3, _) = _vertices[i + 3];
        var size = image is Bitmap b ? new Size(b.PixelSize.Width, b.PixelSize.Height) : image.Size;
        // The affine map from texture space (u, v in 0..1) to the screen, from three corners (works for the
        // atlas' rotated sprites too, where u runs along the quad's other side).
        double a1 = t1.X - t0.X, b1 = t3.X - t0.X, c1 = t1.Y - t0.Y, d1 = t3.Y - t0.Y;
        double det = a1 * d1 - b1 * c1;
        if (Math.Abs(det) < 1e-12) return;
        double ia = d1 / det, ib = -b1 / det, ic = -c1 / det, id = a1 / det;
        var e1 = p1 - p0; var e3 = p3 - p0;
        double mxx = e1.X * ia + e3.X * ic, mxy = e1.X * ib + e3.X * id;
        double myx = e1.Y * ia + e3.Y * ic, myy = e1.Y * ib + e3.Y * id;
        double ox = p0.X - (mxx * t0.X + mxy * t0.Y), oy = p0.Y - (myx * t0.X + myy * t0.Y);
        var m = new AMatrix(mxx, myx, mxy, myy, ox, oy);
        // Only the quad's part of the texture (one sprite of an atlas, not the whole sheet).
        var t2 = _vertices[i + 2].Uv;
        double u0 = Math.Min(Math.Min(t0.X, t1.X), Math.Min(t2.X, t3.X)), u1 = Math.Max(Math.Max(t0.X, t1.X), Math.Max(t2.X, t3.X));
        double v0 = Math.Min(Math.Min(t0.Y, t1.Y), Math.Min(t2.Y, t3.Y)), v1 = Math.Max(Math.Max(t0.Y, t1.Y), Math.Max(t2.Y, t3.Y));
        if (u1 - u0 <= 0 || v1 - v0 <= 0) return;
        var source = new Rect(u0 * size.Width, v0 * size.Height, (u1 - u0) * size.Width, (v1 - v0) * size.Height);
        using (ctx.PushTransform(m))
            Raylib.DrawImage(image, source, new Rect(u0, v0, u1 - u0, v1 - v0), c0);
    }

    private static void FillPolygon(DrawingContext ctx, int i, int count)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new APoint(_vertices[i].Position.X, _vertices[i].Position.Y), true);
            for (int k = 1; k < count; k++)
                g.LineTo(new APoint(_vertices[i + k].Position.X, _vertices[i + k].Position.Y));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(Raylib.Brush(_vertices[i].Color), null, geometry);
    }
}
