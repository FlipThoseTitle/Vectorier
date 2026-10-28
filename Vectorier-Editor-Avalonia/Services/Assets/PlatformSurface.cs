using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Vectorier_Editor_Avalonia.Models.Scene;

namespace Vectorier_Editor_Avalonia.Services.Assets;

public enum PlatformSurfaceType
{
    /// <summary>Random floor and wall tiles.</summary>
    Rough,
    /// <summary>Fixed top corners, v_black covers the top.</summary>
    Smooth,
    /// <summary>Only v_black.</summary>
    Block,
}

/// <summary>
/// Port of the Unity editor's PlatformSurfaceBuilder: dresses a Platform rectangle with Vector's black block
/// textures (corners, floor and wall tiles, v_black fill). Textures are looked up by name in the project's textures.
/// The result is an Object (no definition) holding the Platform and its images, at the platform's position.
/// </summary>
public static class PlatformSurface
{
    private const string CornerUpL = "v_CornerUp_L_01";
    private const string CornerUpR = "v_CornerUp_R_01";
    private const string CornerDownL = "v_CornerDown_L_01";
    private const string CornerDownR = "v_CornerDown_R_01";
    private const string Floor = "v_Floor_01";
    private const string FloorLong = "v_LongFloor_01";
    private const string WallL = "v_Wall_L_01";
    private const string WallR = "v_Wall_R_01";
    private const string WallLLong = "v_LongCornerUp_L_01";
    private const string WallRLong = "v_LongCornerUp_R_01";
    public const string BlackFill = "v_black";

    private const double Epsilon = 0.01;
    private const int MaxTiles = 4096;
    private static readonly Vector SmoothFillInset = new(4, 2);

    private sealed record Sprite(string Name, double Width, double Height);

    /// <summary>
    /// Builds the dressed platform. <paramref name="rect"/> is the platform (level space, Y down);
    /// the returned Object sits at its top-left corner.
    /// </summary>
    public static async Task<ObjectElement> BuildAsync(TextureLibrary textures, PlatformSurfaceType type, Rect rect, int seed,
        bool floor = true, bool leftWall = true, bool rightWall = true)
    {
        var group = new ObjectElement { X = Math.Round(rect.X), Y = Math.Round(rect.Y), IsExpanded = false };
        double w = Math.Max(1, Math.Round(rect.Width)), h = Math.Max(1, Math.Round(rect.Height));
        var images = new List<ImageElement>();
        var rng = new Random(seed == 0 ? Environment.TickCount : seed);

        async Task<Sprite?> Load(string name)
        {
            var asset = await textures.GetAsync(name);
            return asset == null ? null : new Sprite(name, asset.Width, asset.Height);
        }

        async Task<List<Sprite>?> Variants(string basePath)
        {
            var list = new List<Sprite>();
            for (int i = 1; i <= 3; i++)
                if (await Load(basePath.Replace("_01", $"_{i:00}")) is { } s)
                    list.Add(s);
            return list.Count == 0 ? null : list;
        }

        Sprite Pick(List<Sprite> list) => list[rng.Next(list.Count)];

        void Add(Sprite sprite, double x, double y, double? width = null, double? height = null)
        {
            images.Add(new ImageElement
            {
                Name = sprite.Name,
                X = Math.Round(x),
                Y = Math.Round(y),
                Width = width ?? sprite.Width,
                Height = height ?? sprite.Height,
                NativeWidth = (int)sprite.Width,
                NativeHeight = (int)sprite.Height,
                AutoSize = width == null && height == null,
            });
        }

        async Task Fill(double left, double top, double right, double bottom)
        {
            double fw = right - left, fh = bottom - top;
            if (fw <= Epsilon || fh <= Epsilon)
                return;
            if (await Load(BlackFill) is { } fill)
                Add(fill, left, top, Math.Round(fw), Math.Round(fh));
        }

        // Level space: left, top (Y down), right, bottom, relative to the group.
        double L = 0, T = 0, R = w, B = h;
        var behind = new List<ImageElement>();

        if (type == PlatformSurfaceType.Block)
        {
            await Fill(L, T, R, B);
        }
        else
        {
            bool smooth = type == PlatformSurfaceType.Smooth;
            bool wantUpLeft = smooth || floor || leftWall;
            bool wantUpRight = smooth || floor || rightWall;
            var upLeft = wantUpLeft ? (smooth ? await Load(CornerUpL) : (await Variants(CornerUpL)) is { } ul ? Pick(ul) : null) : null;
            var upRight = wantUpRight ? (smooth ? await Load(CornerUpR) : (await Variants(CornerUpR)) is { } ur ? Pick(ur) : null) : null;

            var front = new List<Action>();
            if (upLeft != null) Add(upLeft, L, T);
            if (upRight != null) Add(upRight, R - upRight.Width, T);

            // Floor (Rough only): long tiles while they fit, short ones after, the last one snapped to the end.
            if (!smooth && floor && upLeft != null && upRight != null)
            {
                var shorts = await Variants(Floor);
                var longs = await Variants(FloorLong);
                if (shorts != null && longs != null)
                {
                    double start = L + upLeft.Width, end = R - upRight.Width, x = start;
                    for (int i = 0; i < MaxTiles && end - x > Epsilon; i++)
                    {
                        double remaining = end - x;
                        var chosen = Pick(remaining >= longs[0].Width - Epsilon ? longs : shorts);
                        if (chosen.Width <= 0) break;
                        if (chosen.Width >= remaining - Epsilon)
                        {
                            Add(chosen, end - chosen.Width, T);
                            break;
                        }
                        Add(chosen, x, T);
                        x += chosen.Width;
                    }
                }
            }

            async Task Wall(bool right, Sprite upCorner)
            {
                var downs = await Variants(right ? CornerDownR : CornerDownL);
                if (downs == null)
                    return;
                var down = Pick(downs);
                double downTop = B - down.Height; // top-left of the bottom corner
                Add(down, right ? R - down.Width : L, downTop);
                var shorts = await Variants(right ? WallR : WallL);
                var longs = await Variants(right ? WallRLong : WallLLong);
                if (shorts == null || longs == null)
                    return;
                double y = T + upCorner.Height, bottom = downTop;
                for (int i = 0; i < MaxTiles && bottom - y > Epsilon; i++)
                {
                    double remaining = bottom - y;
                    var chosen = Pick(remaining >= longs[0].Height - Epsilon ? longs : shorts);
                    if (chosen.Height <= 0) break;
                    double x = right ? R - chosen.Width : L;
                    if (chosen.Height >= remaining - Epsilon)
                    {
                        Add(chosen, x, bottom - chosen.Height);
                        break;
                    }
                    Add(chosen, x, y);
                    y += chosen.Height;
                }
            }

            if (leftWall && upLeft != null) await Wall(false, upLeft);
            if (rightWall && upRight != null) await Wall(true, upRight);

            // The fill goes behind the edges.
            int edges = images.Count;
            if (smooth)
            {
                await Fill(L + SmoothFillInset.X, T + SmoothFillInset.Y, R - SmoothFillInset.X, B);
            }
            else
            {
                double left = leftWall && upLeft != null ? L + upLeft.Width : L;
                double right = rightWall && upRight != null ? R - upRight.Width : R;
                double top = floor && upLeft != null ? T + upLeft.Height : T;
                await Fill(left, top, right, B);
            }
            if (images.Count > edges)
            {
                behind.AddRange(images.GetRange(edges, images.Count - edges));
                images.RemoveRange(edges, images.Count - edges);
            }
        }

        foreach (var image in behind)
            group.AddChild(image);
        foreach (var image in images)
            group.AddChild(image);
        group.AddChild(new PlatformElement { X = 0, Y = 0, Width = w, Height = h });
        return group;
    }
}
