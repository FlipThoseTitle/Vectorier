using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Vectorier_Editor_Avalonia.Controls.Scene;

/// <summary>
/// Recolored copies of bitmaps (Image Color / StartColor multiplies the texture, as in the game). Built once
/// per bitmap and color on the CPU; only images with a non-white RGB use it (alpha alone uses opacity).
/// </summary>
public static class TintCache
{
    private const int MaxEntries = 256;
    private static readonly Dictionary<(Bitmap, uint), Bitmap?> Cache = new();
    private static readonly LinkedList<(Bitmap, uint)> Order = new();

    public static bool NeedsTint(Color color) => color.R != 255 || color.G != 255 || color.B != 255;

    public static Bitmap? Get(Bitmap source, Color color)
    {
        // Alpha is applied with opacity, so the cache key ignores it.
        var rgb = Color.FromArgb(255, color.R, color.G, color.B);
        var key = (source, rgb.ToUInt32());
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        Bitmap? tinted = null;
        try
        {
            tinted = Build(source, rgb);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[TintCache] " + ex.Message);
        }

        Cache[key] = tinted;
        Order.AddLast(key);
        if (Order.Count > MaxEntries)
        {
            var oldest = Order.First!.Value;
            Order.RemoveFirst();
            if (Cache.Remove(oldest, out var old) && old is IDisposable d)
                d.Dispose();
        }
        return tinted;
    }

    private static Bitmap Build(Bitmap source, Color tint)
    {
        var size = source.PixelSize;
        int stride = size.Width * 4;
        var buffer = new byte[stride * size.Height];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            source.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), buffer.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        var format = source.Format ?? PixelFormat.Bgra8888;
        bool rgba = format == PixelFormat.Rgba8888;
        double r = tint.R / 255.0, g = tint.G / 255.0, b = tint.B / 255.0;
        double c0 = rgba ? r : b, c2 = rgba ? b : r;

        for (int i = 0; i < buffer.Length; i += 4)
        {
            // Multiplying color channels keeps premultiplied data valid too.
            buffer[i] = (byte)(buffer[i] * c0);
            buffer[i + 1] = (byte)(buffer[i + 1] * g);
            buffer[i + 2] = (byte)(buffer[i + 2] * c2);
        }

        var alpha = source.AlphaFormat ?? AlphaFormat.Premul;
        var result = new WriteableBitmap(size, source.Dpi, format, alpha);
        using (var frame = result.Lock())
        {
            for (int y = 0; y < size.Height; y++)
                Marshal.Copy(buffer, y * stride, frame.Address + y * frame.RowBytes, stride);
        }
        return result;
    }
}
