using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Vectorier.Services.Assets;

public enum BrushKind
{
    Hard,
    Soft,
    Airbrush,
    /// <summary>Softens what is already painted (soft edge); color, blending and Erase do not apply.</summary>
    Blur,
}

public enum PaintBlendMode
{
    Normal,
    Multiply,
    Screen,
    Overlay,
    Darken,
    Lighten,
    Add,
    Erase,
}

/// <summary>Brush settings for one stroke.</summary>
public readonly record struct BrushSettings(BrushKind Kind, double Diameter, double Opacity, Color Color, PaintBlendMode Blend);

/// <summary>
/// Pixel canvas of the Brush tool: premultiplied BGRA pixels mapped into a layer's level space by
/// <see cref="CanvasToLevel"/>. Strokes build a coverage mask over the pixels as they were when the stroke began,
/// so a stroke never builds up over itself (Hard, Soft); the Airbrush keeps adding paint while held.
/// The canvas can grow (free painting) up to <see cref="MaxSize"/> on each side.
/// </summary>
public sealed class PaintCanvas
{
    /// <summary>Largest image the Brush tool makes or edits, per side. A power of two: the GPU's favourite size.</summary>
    public const int MaxSize = 4096;

    private const int GrowMargin = 128;

    private byte[] _pixels;      // current pixels (premultiplied BGRA)
    private byte[] _base;        // pixels when the stroke began
    private float[] _mask;       // stroke coverage 0..1
    private byte[]? _blurred;    // Blur brush: the stroke's starting pixels, blurred (tile by tile, on demand)
    private bool[]? _blurTiles;
    private int _blurTilesX, _blurRadius;
    private const int BlurTile = 128;
    private byte[]? _clipAlpha;  // alpha of the starting image (Clip to Image follows it)
    private bool _inStroke;
    private PixelRect _strokeRect; // area touched by the stroke (canvas pixels)
    private BrushSettings _brush;
    private Point? _lastDab;
    /// <summary>One stroke to take back: the pixels it covered, and the canvas frame before it (it may have grown).</summary>
    private readonly record struct UndoStep(PixelRect Rect, byte[] Pixels, int FrameWidth, int FrameHeight, int FrameLeft, int FrameTop);

    private readonly List<UndoStep> _undo = new();

    // How far the canvas has grown to the left / top since it was made (the frame of an undo step is relative to it).
    private int _grownLeft, _grownTop;
    private (int Width, int Height, int Left, int Top) _strokeFrame;

    private bool _hasStartContent;
    private PixelRect? _paintedBounds;
    private WriteableBitmap? _bitmap;
    private PixelRect? _bitmapDirty;

    public PaintCanvas(int width, int height, Matrix canvasToLevel, bool canGrow)
    {
        Width = Math.Clamp(width, 1, MaxSize);
        Height = Math.Clamp(height, 1, MaxSize);
        CanvasToLevel = canvasToLevel;
        CanGrow = canGrow;
        _pixels = new byte[Width * Height * 4];
        _base = new byte[_pixels.Length];
        _mask = new float[Width * Height];
        Original = new PixelRect(0, 0, Width, Height);
    }

    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>Canvas pixel → level space of the canvas's layer.</summary>
    public Matrix CanvasToLevel { get; private set; }

    /// <summary>Free painting: the canvas grows when the brush leaves it (axis-aligned canvases only).</summary>
    public bool CanGrow { get; }

    /// <summary>Area of the starting content (the edited image) inside the canvas; it moves when the canvas grows to the left or top.</summary>
    public PixelRect Original { get; private set; }

    /// <summary>
    /// When true, paint stays on the visible (non-transparent) pixels of the starting image: its alpha masks the
    /// brush, so transparent surroundings are never filled (Clip to Image).
    /// </summary>
    public bool ClipToOriginal { get; set; }

    public bool HasUndo => _undo.Count > 0;

    /// <summary>Something was painted since the canvas was created.</summary>
    public bool IsModified { get; private set; }

    /// <summary>Level units per canvas pixel (average of both axes).</summary>
    public double PixelScale => Math.Sqrt(Math.Abs(CanvasToLevel.M11 * CanvasToLevel.M22 - CanvasToLevel.M12 * CanvasToLevel.M21));

    // ================= CONTENT ================= //

    /// <summary>Fills the canvas from premultiplied BGRA pixels of the same size (the starting image).</summary>
    public void Load(byte[] premultipliedBgra)
    {
        Buffer.BlockCopy(premultipliedBgra, 0, _pixels, 0, Math.Min(premultipliedBgra.Length, _pixels.Length));
        _hasStartContent = true;
        _paintedBounds = Original;
        _clipAlpha = new byte[Width * Height];
        for (int i = 0; i < _clipAlpha.Length; i++)
            _clipAlpha[i] = _pixels[i * 4 + 3];
        MarkBitmapDirty(new PixelRect(0, 0, Width, Height));
    }

    /// <summary>Copy of the current pixels inside <paramref name="rect"/> (premultiplied BGRA).</summary>
    public byte[] CopyRegion(PixelRect rect)
    {
        var data = new byte[rect.Width * rect.Height * 4];
        for (int y = 0; y < rect.Height; y++)
            Buffer.BlockCopy(_pixels, ((rect.Y + y) * Width + rect.X) * 4, data, y * rect.Width * 4, rect.Width * 4);
        return data;
    }

    /// <summary>Smallest rectangle holding every pixel with some alpha, or null when the canvas is empty.</summary>
    public PixelRect? ContentBounds()
    {
        int minX = Width, minY = Height, maxX = -1, maxY = -1;
        for (int y = 0; y < Height; y++)
        {
            int row = y * Width * 4;
            for (int x = 0; x < Width; x++)
            {
                if (_pixels[row + x * 4 + 3] == 0)
                    continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        return maxX < 0 ? null : new PixelRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    // ================= STROKES ================= //

    public Point LevelToCanvas(Point level) => CanvasToLevel.Invert().Transform(level);

    public void BeginStroke(BrushSettings brush, Point canvasPoint)
    {
        if (_inStroke)
            EndStroke();
        _brush = brush;
        _inStroke = true;
        _strokeFrame = (Width, Height, _grownLeft, _grownTop);
        _strokeRect = default;
        _lastDab = null;
        Buffer.BlockCopy(_pixels, 0, _base, 0, _pixels.Length);
        // Blur: a blurred copy of the canvas, faded in where the brush passes (strength = opacity).
        // Computed tile by tile where the brush goes, so a big canvas does not wait for a full blur.
        if (brush.Kind == BrushKind.Blur)
        {
            _blurred = new byte[_base.Length];
            _blurRadius = BlurRadius(brush);
            _blurTilesX = (Width + BlurTile - 1) / BlurTile;
            _blurTiles = new bool[_blurTilesX * ((Height + BlurTile - 1) / BlurTile)];
        }
        else
        {
            _blurred = null;
            _blurTiles = null;
        }
        Array.Clear(_mask);
        StrokeTo(canvasPoint);
    }

    /// <summary>Continues the stroke to a point, dabbing along the way.</summary>
    public void StrokeTo(Point canvasPoint)
    {
        if (!_inStroke)
            return;
        double radius = Radius;
        if (_lastDab is not { } last)
        {
            _lastDab = canvasPoint + Dab(canvasPoint);
            return;
        }
        double spacing = Math.Max(0.5, radius * (_brush.Kind == BrushKind.Hard ? 0.12 : 0.18));
        var pos = last;
        var target = canvasPoint;
        while (true)
        {
            var delta = target - pos;
            double length = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
            if (length < spacing)
                break;
            pos += delta * (spacing / length);
            // Growing the canvas shifts its pixels: keep both points in the new frame.
            var shift = Dab(pos);
            pos += shift;
            target += shift;
        }
        _lastDab = pos;
    }

    /// <summary>Airbrush: keeps spraying at the last point while the button is held.</summary>
    public void Spray()
    {
        if (_inStroke && _brush.Kind == BrushKind.Airbrush && _lastDab is { } p)
            _lastDab = p + Dab(p);
    }

    public void EndStroke()
    {
        if (!_inStroke)
            return;
        _inStroke = false;
        _blurred = null;
        _blurTiles = null;
        if (_strokeRect.Width <= 0 || _strokeRect.Height <= 0)
            return;
        // Undo keeps the pixels of the touched area from before the stroke.
        var rect = _strokeRect;
        var saved = new byte[rect.Width * rect.Height * 4];
        for (int y = 0; y < rect.Height; y++)
            Buffer.BlockCopy(_base, ((rect.Y + y) * Width + rect.X) * 4, saved, y * rect.Width * 4, rect.Width * 4);
        _undo.Add(new UndoStep(rect, saved, _strokeFrame.Width, _strokeFrame.Height, _strokeFrame.Left, _strokeFrame.Top));
        if (_undo.Count > 40)
            _undo.RemoveAt(0);
        IsModified = true;
        _paintedBounds = _paintedBounds is { } b ? b.Union(rect) : rect;
    }

    public bool Undo()
    {
        if (_inStroke || _undo.Count == 0)
            return false;
        var step = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        var rect = step.Rect;
        for (int y = 0; y < rect.Height; y++)
            Buffer.BlockCopy(step.Pixels, y * rect.Width * 4, _pixels, ((rect.Y + y) * Width + rect.X) * 4, rect.Width * 4);
        MarkBitmapDirty(rect);

        // The stroke made the canvas grow: shrink it back to the frame it had before.
        if (step.FrameWidth != Width || step.FrameHeight != Height || step.FrameLeft != _grownLeft || step.FrameTop != _grownTop)
            Crop(_grownLeft - step.FrameLeft, _grownTop - step.FrameTop, step.FrameWidth, step.FrameHeight);

        RecomputePaintedBounds();
        return true;
    }

    /// <summary>
    /// What will be kept: everything painted, plus the edited image's own area. The outline shown while painting,
    /// and it follows undo exactly.
    /// </summary>
    public PixelRect? PaintedBounds => _paintedBounds;

    private void RecomputePaintedBounds()
    {
        var content = ContentBounds();
        _paintedBounds = _hasStartContent
            ? (content is { } c ? Original.Union(c) : Original)
            : content;
    }

    /// <summary>Keeps only the frame (x, y, width, height) of the canvas (undoing a growth).</summary>
    private void Crop(int x, int y, int width, int height)
    {
        x = Math.Clamp(x, 0, Width - 1);
        y = Math.Clamp(y, 0, Height - 1);
        width = Math.Clamp(width, 1, Width - x);
        height = Math.Clamp(height, 1, Height - y);
        T[] Cut<T>(T[] source, int bpp)
        {
            var target = new T[width * height * bpp];
            for (int row = 0; row < height; row++)
                Array.Copy(source, ((row + y) * Width + x) * bpp, target, row * width * bpp, width * bpp);
            return target;
        }
        _pixels = Cut(_pixels, 4);
        _base = Cut(_base, 4);
        _blurred = null; // only used during a Blur stroke, which never resizes the canvas
        _mask = Cut(_mask, 1);
        if (_clipAlpha != null)
            _clipAlpha = Cut(_clipAlpha, 1);
        var shift = new PixelVector(-x, -y);
        for (int i = 0; i < _undo.Count; i++)
            _undo[i] = _undo[i] with { Rect = _undo[i].Rect.Translate(shift) };
        Original = Original.Translate(shift);
        CanvasToLevel = Matrix.CreateTranslation(x, y) * CanvasToLevel;
        _grownLeft -= x;
        _grownTop -= y;
        Width = width;
        Height = height;
        _bitmap = null;
        _bitmapDirty = new PixelRect(0, 0, width, height);
    }

    private double Radius => Math.Max(0.5, _brush.Diameter / 2 / Math.Max(1e-6, PixelScale));

    /// <summary>One dab of the brush. Returns how far the canvas content moved if it had to grow.</summary>
    private Vector Dab(Point c)
    {
        double r = Radius;
        var shift = EnsureRoom(c, r);
        c += shift;

        var limit = ClipToOriginal ? Original : new PixelRect(0, 0, Width, Height);
        int x0 = Math.Max(limit.X, (int)Math.Floor(c.X - r - 1));
        int y0 = Math.Max(limit.Y, (int)Math.Floor(c.Y - r - 1));
        int x1 = Math.Min(limit.Right, (int)Math.Ceiling(c.X + r + 1));
        int y1 = Math.Min(limit.Bottom, (int)Math.Ceiling(c.Y + r + 1));
        if (x1 <= x0 || y1 <= y0)
            return shift;

        float opacity = (float)Math.Clamp(_brush.Opacity, 0, 1);
        // Airbrush flow: each spray adds a small part of the way towards the opacity.
        float flow = 0.1f;
        var color = _brush.Color;
        float sr = color.R / 255f, sg = color.G / 255f, sb = color.B / 255f;
        var blend = _brush.Blend;
        double rr = r * r;

        for (int y = y0; y < y1; y++)
        {
            double dy = y + 0.5 - c.Y;
            for (int x = x0; x < x1; x++)
            {
                double dx = x + 0.5 - c.X;
                double d2 = dx * dx + dy * dy;
                float a;
                switch (_brush.Kind)
                {
                    case BrushKind.Hard:
                        if (d2 > (r + 1) * (r + 1)) continue;
                        a = (float)Math.Clamp(r - Math.Sqrt(d2) + 0.5, 0, 1);
                        break;
                    default:
                        if (d2 >= rr) continue;
                        double t = 1 - d2 / rr;
                        a = (float)(t * t);
                        break;
                }
                if (a <= 0)
                    continue;
                int i = y * Width + x;
                if (ClipToOriginal && _clipAlpha != null)
                {
                    byte clip = _clipAlpha[i];
                    if (clip == 0)
                        continue;
                    a *= clip / 255f;
                }
                float m = _mask[i];
                float next = _brush.Kind == BrushKind.Airbrush
                    ? m + (opacity - m) * a * flow
                    : Math.Max(m, a * opacity);
                if (next <= m)
                    continue;
                _mask[i] = next;
                if (_blurred != null)
                {
                    EnsureBlurred(x, y);
                    Mix(i * 4, next);
                }
                else
                    Composite(i * 4, next, sr, sg, sb, blend);
            }
        }

        var dab = new PixelRect(x0, y0, x1 - x0, y1 - y0);
        _strokeRect = _strokeRect.Width <= 0 ? dab : _strokeRect.Union(dab);
        MarkBitmapDirty(dab);
        return shift;
    }

    /// <summary>Blur: the starting pixel faded towards its blurred value (premultiplied, so edges stay clean).</summary>
    private void Mix(int o, float m)
    {
        var blurred = _blurred!;
        for (int c = 0; c < 4; c++)
            _pixels[o + c] = (byte)Math.Clamp((int)(_base[o + c] + (blurred[o + c] - _base[o + c]) * m + 0.5f), 0, 255);
    }

    /// <summary>Blurs the tile holding (x, y) if it is not done yet (from the pixels the stroke started with).</summary>
    private void EnsureBlurred(int x, int y)
    {
        int tx = x / BlurTile, ty = y / BlurTile, index = ty * _blurTilesX + tx;
        if (_blurTiles![index])
            return;
        _blurTiles[index] = true;
        // The tile plus a margin covering how far the two box passes reach.
        int margin = _blurRadius * 2 + 1;
        int x0 = Math.Max(0, tx * BlurTile - margin), y0 = Math.Max(0, ty * BlurTile - margin);
        int x1 = Math.Min(Width, (tx + 1) * BlurTile + margin), y1 = Math.Min(Height, (ty + 1) * BlurTile + margin);
        int w = x1 - x0, h = y1 - y0;
        var region = new byte[w * h * 4];
        for (int row = 0; row < h; row++)
            Buffer.BlockCopy(_base, ((y0 + row) * Width + x0) * 4, region, row * w * 4, w * 4);
        var blurred = BlurCopy(region, w, h, _blurRadius);
        int cx0 = tx * BlurTile, cy0 = ty * BlurTile;
        int cx1 = Math.Min(Width, cx0 + BlurTile), cy1 = Math.Min(Height, cy0 + BlurTile);
        for (int row = cy0; row < cy1; row++)
            Buffer.BlockCopy(blurred, ((row - y0) * w + (cx0 - x0)) * 4, _blurred!, (row * Width + cx0) * 4, (cx1 - cx0) * 4);
    }

    /// <summary>Blur radius in canvas pixels: a third of the brush radius (1 – 32).</summary>
    private double BlurRadiusFor(BrushSettings brush) => brush.Diameter / 2 / Math.Max(1e-6, PixelScale) / 3;

    private int BlurRadius(BrushSettings brush) => Math.Clamp((int)Math.Round(BlurRadiusFor(brush)), 1, 32);

    /// <summary>Two box blurs in each direction (close to a Gaussian), rows and columns in parallel.</summary>
    private static byte[] BlurCopy(byte[] source, int width, int height, int radius)
    {
        var a = (byte[])source.Clone();
        var b = new byte[source.Length];
        for (int pass = 0; pass < 2; pass++)
        {
            BoxBlur(a, b, width, height, radius, horizontal: true);
            BoxBlur(b, a, width, height, radius, horizontal: false);
        }
        return a;
    }

    private static void BoxBlur(byte[] src, byte[] dst, int width, int height, int r, bool horizontal)
    {
        int lines = horizontal ? height : width;
        int length = horizontal ? width : height;
        int step = horizontal ? 4 : width * 4;
        System.Threading.Tasks.Parallel.For(0, lines, line =>
        {
            int start = horizontal ? line * width * 4 : line * 4;
            float scale = 1f / (2 * r + 1);
            for (int c = 0; c < 4; c++)
            {
                // Running sum over the window, edges clamped.
                int sum = 0;
                for (int k = -r; k <= r; k++)
                    sum += src[start + Math.Clamp(k, 0, length - 1) * step + c];
                for (int i = 0; i < length; i++)
                {
                    dst[start + i * step + c] = (byte)(sum * scale + 0.5f);
                    int outIndex = Math.Clamp(i - r, 0, length - 1);
                    int inIndex = Math.Clamp(i + r + 1, 0, length - 1);
                    sum += src[start + inIndex * step + c] - src[start + outIndex * step + c];
                }
            }
        });
    }

    /// <summary>Base pixel (from before the stroke) covered by the stroke mask <paramref name="m"/>.</summary>
    private void Composite(int o, float m, float sr, float sg, float sb, PaintBlendMode blend)
    {
        float bb = _base[o] / 255f, bg = _base[o + 1] / 255f, br = _base[o + 2] / 255f, ba = _base[o + 3] / 255f;
        float outR, outG, outB, outA;
        if (blend == PaintBlendMode.Erase)
        {
            float k = 1 - m;
            outR = br * k; outG = bg * k; outB = bb * k; outA = ba * k;
        }
        else
        {
            // Unpremultiplied base color for the blend function.
            float cr = ba > 0 ? br / ba : 0, cg = ba > 0 ? bg / ba : 0, cb = ba > 0 ? bb / ba : 0;
            float fr = Blend(blend, cr, sr), fg = Blend(blend, cg, sg), fb = Blend(blend, cb, sb);
            // Where the base is transparent the brush color shows as is.
            float mixR = (1 - ba) * sr + ba * fr, mixG = (1 - ba) * sg + ba * fg, mixB = (1 - ba) * sb + ba * fb;
            outR = br * (1 - m) + mixR * m;
            outG = bg * (1 - m) + mixG * m;
            outB = bb * (1 - m) + mixB * m;
            outA = ba + m * (1 - ba);
        }
        _pixels[o] = ToByte(outB);
        _pixels[o + 1] = ToByte(outG);
        _pixels[o + 2] = ToByte(outR);
        _pixels[o + 3] = ToByte(outA);
    }

    private static float Blend(PaintBlendMode mode, float b, float s) => mode switch
    {
        PaintBlendMode.Multiply => b * s,
        PaintBlendMode.Screen => 1 - (1 - b) * (1 - s),
        PaintBlendMode.Overlay => b < 0.5f ? 2 * b * s : 1 - 2 * (1 - b) * (1 - s),
        PaintBlendMode.Darken => Math.Min(b, s),
        PaintBlendMode.Lighten => Math.Max(b, s),
        PaintBlendMode.Add => Math.Min(1, b + s),
        _ => s,
    };

    private static byte ToByte(float v) => (byte)Math.Clamp((int)(v * 255f + 0.5f), 0, 255);

    // ================= GROWTH ================= //

    /// <summary>Grows the canvas so a dab at <paramref name="c"/> fits (free painting). Returns the content shift.</summary>
    private Vector EnsureRoom(Point c, double r)
    {
        if (!CanGrow || ClipToOriginal || _brush.Kind == BrushKind.Blur) // nothing to blur outside
            return default;
        int needLeft = (int)Math.Ceiling(r + 1 - c.X);
        int needTop = (int)Math.Ceiling(r + 1 - c.Y);
        int needRight = (int)Math.Ceiling(c.X + r + 1 - Width);
        int needBottom = (int)Math.Ceiling(c.Y + r + 1 - Height);
        if (needLeft <= 0 && needTop <= 0 && needRight <= 0 && needBottom <= 0)
            return default;

        int left = needLeft > 0 ? needLeft + GrowMargin : 0;
        int top = needTop > 0 ? needTop + GrowMargin : 0;
        int right = needRight > 0 ? needRight + GrowMargin : 0;
        int bottom = needBottom > 0 ? needBottom + GrowMargin : 0;
        // Never past the largest size: what does not fit is clipped.
        int roomX = MaxSize - Width, roomY = MaxSize - Height;
        if (left + right > roomX)
        {
            double k = roomX / (double)(left + right);
            left = (int)(left * k);
            right = roomX - left;
        }
        if (top + bottom > roomY)
        {
            double k = roomY / (double)(top + bottom);
            top = (int)(top * k);
            bottom = roomY - top;
        }
        if (left + right + top + bottom == 0)
            return default;
        Resize(left, top, right, bottom);
        return new Vector(left, top);
    }

    private void Resize(int left, int top, int right, int bottom)
    {
        int w = Width + left + right, h = Height + top + bottom;
        byte[] Shift(byte[] source, int bpp)
        {
            var target = new byte[w * h * bpp];
            for (int y = 0; y < Height; y++)
                Buffer.BlockCopy(source, y * Width * bpp, target, ((y + top) * w + left) * bpp, Width * bpp);
            return target;
        }
        _pixels = Shift(_pixels, 4);
        _base = Shift(_base, 4);
        var mask = new float[w * h];
        for (int y = 0; y < Height; y++)
            Array.Copy(_mask, y * Width, mask, (y + top) * w + left, Width);
        _mask = mask;
        if (_clipAlpha != null)
        {
            var clip = new byte[w * h];
            for (int y = 0; y < Height; y++)
                Buffer.BlockCopy(_clipAlpha, y * Width, clip, (y + top) * w + left, Width);
            _clipAlpha = clip;
        }
        for (int i = 0; i < _undo.Count; i++)
            _undo[i] = _undo[i] with { Rect = _undo[i].Rect.Translate(new PixelVector(left, top)) };
        if (_paintedBounds is { } painted)
            _paintedBounds = painted.Translate(new PixelVector(left, top));
        _grownLeft += left;
        _grownTop += top;
        if (_strokeRect.Width > 0)
            _strokeRect = _strokeRect.Translate(new PixelVector(left, top));
        Original = Original.Translate(new PixelVector(left, top));
        // Pixel (0,0) moves to (-left, -top) of the old frame.
        CanvasToLevel = Matrix.CreateTranslation(-left, -top) * CanvasToLevel;
        Width = w;
        Height = h;
        _bitmap = null; // recreated at the new size
        _bitmapDirty = new PixelRect(0, 0, w, h);
    }

    // ================= DISPLAY ================= //

    private void MarkBitmapDirty(PixelRect rect)
        => _bitmapDirty = _bitmapDirty is { } d ? d.Union(rect) : rect;

    /// <summary>The canvas as a bitmap for drawing, updated with what changed since the last call.</summary>
    public WriteableBitmap GetBitmap()
    {
        if (_bitmap == null)
        {
            _bitmap = new WriteableBitmap(new PixelSize(Width, Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            _bitmapDirty = new PixelRect(0, 0, Width, Height);
        }
        if (_bitmapDirty is { } dirty)
        {
            _bitmapDirty = null;
            using var fb = _bitmap.Lock();
            int rowBytes = fb.RowBytes;
            for (int y = dirty.Y; y < dirty.Bottom; y++)
                Marshal.Copy(_pixels, (y * Width + dirty.X) * 4, fb.Address + y * rowBytes + dirty.X * 4, dirty.Width * 4);
        }
        return _bitmap;
    }

    /// <summary>PNG of a part of the canvas.</summary>
    public byte[] EncodePng(PixelRect rect)
    {
        var data = CopyRegion(rect);
        using var bitmap = new WriteableBitmap(new PixelSize(rect.Width, rect.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = bitmap.Lock())
            for (int y = 0; y < rect.Height; y++)
                Marshal.Copy(data, y * rect.Width * 4, fb.Address + y * fb.RowBytes, rect.Width * 4);
        using var stream = new System.IO.MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        return stream.ToArray();
    }
}
