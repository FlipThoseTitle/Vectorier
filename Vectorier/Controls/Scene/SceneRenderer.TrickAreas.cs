using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Vectorier.Models.Scene;
using Vectorier.Services;
using Vectorier.Services.Assets;
using Vectorier.Services.IO;

namespace Vectorier.Controls.Scene;

public partial class SceneRenderer
{
    private readonly Dictionary<string, Bitmap?> _trackImages = new(StringComparer.Ordinal);

    /// <summary>
    /// How a Trick Area looks in the game (Unity AreaElement.CreateTrickVisuals): the animated trick_idle_up at the
    /// Area's center, and the trick's TRACK_ image (icons/tricks) 23.5 units below it, scaled 0.906 × 0.914.
    /// Returns whether something animated was drawn.
    /// </summary>
    private bool DrawTrickVisuals(DrawingContext context, AreaElement area, Point o, Matrix m, double seconds)
    {
        var b = area.GetLocalBounds();
        var center = new Point(o.X + b.Center.X, o.Y + b.Center.Y);
        bool animated = false;
        if (_editor.Context.Textures.TryGet("trick_idle_up") is { } idle)
        {
            var local = new Matrix(idle.Width, 0, 0, idle.Height, center.X - idle.Width / 2.0, center.Y - idle.Height / 2.0);
            DrawAsset(context, idle, local * m, Colors.White, seconds);
            animated = idle.IsAnimated;
        }
        if (string.IsNullOrEmpty(area.ItemName))
            return animated;
        string name = "TRACK_" + area.ItemName;
        if (!_trackImages.TryGetValue(name, out var track))
        {
            _trackImages[name] = null;
            _ = LoadTrackImageAsync(name);
            return animated;
        }
        if (track != null)
        {
            double w = track.PixelSize.Width * 0.9059364, h = track.PixelSize.Height * 0.9138624;
            var tc = new Point(center.X, center.Y + 23.5259);
            var a = new Point(tc.X - w / 2, tc.Y - h / 2).Transform(m);
            var c = new Point(tc.X + w / 2, tc.Y + h / 2).Transform(m);
            context.DrawImage(track, new Rect(0, 0, track.PixelSize.Width, track.PixelSize.Height), RectEx.Box(a, c));
        }
        return animated;
    }

    private async Task LoadTrackImageAsync(string name)
    {
        string folder = VPath.Combine(_editor.Project.Path, "icons", "tricks");
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
        {
            string path = VPath.Combine(folder, name + ext);
            try
            {
                if (!await AppServices.FileSystem.FileExistsAsync(path))
                    continue;
                _trackImages[name] = await ImageLoader.LoadAsync(AppServices.FileSystem, path);
                RedrawRequested?.Invoke();
            }
            catch
            {
                // shown without the trick image
            }
            return;
        }
    }

    /// <summary>The Spawn-style marker (Assets/Gizmos/mark.png) tinted, 100 level units, centered on a level point.</summary>
    public static void DrawMarker(DrawingContext context, Matrix levelToScreen, Point center, Color color)
    {
        var a = new Point(center.X - 50, center.Y - 50).Transform(levelToScreen);
        var b = new Point(center.X + 50, center.Y + 50).Transform(levelToScreen);
        var r = RectEx.Box(a, b);
        var mark = MarkBitmap;
        if (mark != null)
        {
            var tinted = TintCache.Get(mark, color) ?? mark;
            context.DrawImage(tinted, new Rect(0, 0, tinted.PixelSize.Width, tinted.PixelSize.Height), r);
        }
        else
        {
            var pen = new Pen(new SolidColorBrush(color), 3);
            context.DrawLine(pen, r.TopLeft, r.BottomRight);
            context.DrawLine(pen, r.TopRight, r.BottomLeft);
        }
    }

    /// <summary>The Spawn gizmo (also drawn by the Project Window's object previews).</summary>
    internal static Bitmap? MarkBitmap
    {
        get
        {
            if (field != null)
                return field;
            try
            {
                using var stream = AssetLoader.Open(new Uri("avares://Vectorier.Core/Assets/Gizmos/mark.png"));
                field = new Bitmap(stream);
            }
            catch
            {
                field = null;
            }
            return field;
        }
    }
}
