using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Vectorier.Services.IO;

namespace Vectorier.Services.Assets;

/// <summary>A texture file of the project (static image or sprite sheet with a .plist/.json).</summary>
public sealed class TextureEntry
{
    public TextureEntry(string name, string path, string? atlasPath)
    {
        Name = name;
        Path = path;
        AtlasPath = atlasPath;
    }

    /// <summary>File name without extension: the ClassName used in XML.</summary>
    public string Name { get; }
    public string Path { get; }
    public string? AtlasPath { get; }
    public bool IsAnimated => AtlasPath != null;
    public string FileName => VPath.GetFileName(Path);
    public string Extension => VPath.GetExtension(Path);
}

/// <summary>A loaded texture. For sprite sheets, <see cref="Frames"/> lists the animation frames.</summary>
public sealed class TextureAsset
{
    public TextureAsset(TextureEntry entry, Bitmap bitmap, IReadOnlyList<AtlasFrame>? frames)
    {
        Entry = entry;
        Bitmap = bitmap;
        Frames = frames is { Count: > 0 } ? frames : null;
        var size = Frames != null ? Frames[0].RealSize : new Size(bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        Width = (int)Math.Round(size.Width);
        Height = (int)Math.Round(size.Height);
    }

    public TextureEntry Entry { get; }
    public Bitmap Bitmap { get; }
    public IReadOnlyList<AtlasFrame>? Frames { get; }
    public bool IsAnimated => Frames != null;

    /// <summary>Logical size (first frame's untrimmed size for sprite sheets).</summary>
    public int Width { get; }
    public int Height { get; }

    /// <summary>Animation frames play at 20 FPS like the game's AnimationSprite.</summary>
    public const double FramesPerSecond = 20;

    public int FrameAt(double seconds) => Frames == null ? 0 : (int)(seconds * FramesPerSecond) % Frames.Count;

    /// <summary>Draws the texture (or one frame) stretched over <paramref name="dest"/>.</summary>
    public void Draw(DrawingContext context, Rect dest, int frame = 0, Bitmap? tinted = null)
    {
        var Bitmap = tinted ?? this.Bitmap;
        if (Frames == null)
        {
            context.DrawImage(Bitmap, new Rect(0, 0, Bitmap.PixelSize.Width, Bitmap.PixelSize.Height), dest);
            return;
        }

        var f = Frames[Math.Clamp(frame, 0, Frames.Count - 1)];
        double sx = dest.Width / Math.Max(1, f.RealSize.Width);
        double sy = dest.Height / Math.Max(1, f.RealSize.Height);
        var box = new Rect(dest.X + f.ResultBox.X * sx, dest.Y + f.ResultBox.Y * sy, f.ResultBox.Width * sx, f.ResultBox.Height * sy);

        if (!f.Rotated)
        {
            context.DrawImage(Bitmap, f.Source, box);
            return;
        }

        // Stored rotated 90° clockwise: rotate back counter-clockwise into the box.
        var transform = Matrix.CreateRotation(-Math.PI / 2) * Matrix.CreateTranslation(box.X, box.Y + box.Height);
        using (context.PushTransform(transform))
            context.DrawImage(Bitmap, f.Source, new Rect(0, 0, box.Height, box.Width));
    }
}
