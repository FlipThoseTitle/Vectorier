using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Vectorier.Models.Scene;

namespace Vectorier.Services.Assets;

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
/// Unity's PlatformComponent: how a Platform is dressed. Kept on the Platform (editor only, never written to the
/// game XML) so the textures can be rebuilt whenever the Platform changes.
/// </summary>
public sealed record PlatformSurfaceSettings(
    PlatformSurfaceType Type,
    bool Floor = true,
    bool LeftWall = true,
    bool RightWall = true,
    int Seed = 1)
{
    public static int NewSeed() => Random.Shared.Next(1, int.MaxValue);
}

/// <summary>
/// Port of the Unity editor's PlatformSurfaceBuilder: dresses a Platform rectangle with Vector's black block
/// textures (corners, floor and wall tiles, v_black fill). Textures are looked up by name in the project's textures.
/// The generated images are siblings of the Platform (inside its Object) and remember which Platform owns them
/// (<see cref="ImageElement.SurfaceOwner"/>), so they can be rebuilt live while the Platform is edited
/// (Unity's PlatformSurfaceWatcher).
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

    /// <summary>Bump when the generation changes, so existing surfaces rebuild the next time they are touched.</summary>
    private const int BuildVersion = 2;

    private const double Epsilon = 0.01;
    private const int MaxTiles = 4096;
    private static readonly Vector SmoothFillInset = new(4, 2);

    private static readonly string[] BaseNames =
        { CornerUpL, CornerUpR, CornerDownL, CornerDownR, Floor, FloorLong, WallL, WallR, WallLLong, WallRLong };

    private sealed record Sprite(string Name, double Width, double Height);

    // Texture sizes, cached per library: the live rebuild runs on every drag step and must not wait for files.
    private static TextureLibrary? _catalogOwner;
    private static Dictionary<string, Sprite> _catalog = new(StringComparer.OrdinalIgnoreCase);
    private static Task? _catalogLoad;

    /// <summary>Every texture name a surface can use (variants _01 to _03 and v_black).</summary>
    public static IEnumerable<string> AllTextureNames()
    {
        foreach (var name in BaseNames)
            for (int i = 1; i <= 3; i++)
                yield return name.Replace("_01", $"_{i:00}");
        yield return BlackFill;
    }

    /// <summary>True when an image name is one a surface generates (used to adopt tiles of saved levels).</summary>
    public static bool IsSurfaceTexture(string name) =>
        string.Equals(name, BlackFill, StringComparison.OrdinalIgnoreCase)
        || BaseNames.Any(b => name.StartsWith(b[..^2], StringComparison.OrdinalIgnoreCase));

    public static bool IsCatalogReady(TextureLibrary textures) => ReferenceEquals(_catalogOwner, textures) && _catalogLoad is { IsCompleted: true };

    /// <summary>Loads the sizes of the surface textures (once per library; <paramref name="reload"/> forces it).</summary>
    public static Task EnsureCatalogAsync(TextureLibrary textures, bool reload = false)
    {
        if (!reload && ReferenceEquals(_catalogOwner, textures) && _catalogLoad != null)
            return _catalogLoad;
        _catalogOwner = textures;
        _catalogLoad = LoadCatalogAsync(textures);
        return _catalogLoad;
    }

    private static async Task LoadCatalogAsync(TextureLibrary textures)
    {
        var catalog = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in AllTextureNames())
        {
            try
            {
                if (await textures.GetAsync(name) is { } asset)
                    catalog[name] = new Sprite(name, asset.Width, asset.Height);
            }
            catch
            {
                // a broken texture is simply not used
            }
        }
        _catalog = catalog;
    }

    /// <summary>Identifies what a surface was built from; equal keys mean the tiles are up to date.</summary>
    public static string BuildKey(PlatformElement platform, PlatformSurfaceSettings s) =>
        $"{BuildVersion}|{s.Type}|{s.Floor}|{s.LeftWall}|{s.RightWall}|{s.Seed}|{Math.Round(platform.X)}|{Math.Round(platform.Y)}|{Math.Round(platform.Width)}|{Math.Round(platform.Height)}";

    /// <summary>
    /// Builds a dressed platform for placement. <paramref name="rect"/> is the platform (level space, Y down);
    /// the returned Object sits at its top-left corner and holds the images and the Platform (with its settings).
    /// </summary>
    public static async Task<ObjectElement> BuildAsync(TextureLibrary textures, PlatformSurfaceType type, Rect rect, int seed,
        bool floor = true, bool leftWall = true, bool rightWall = true)
    {
        await EnsureCatalogAsync(textures);
        var group = new ObjectElement { X = Math.Round(rect.X), Y = Math.Round(rect.Y), IsExpanded = false };
        var settings = new PlatformSurfaceSettings(type, floor, leftWall, rightWall, seed == 0 ? PlatformSurfaceSettings.NewSeed() : seed);
        var platform = new PlatformElement
        {
            X = 0,
            Y = 0,
            Width = Math.Max(1, Math.Round(rect.Width)),
            Height = Math.Max(1, Math.Round(rect.Height)),
            Surface = settings,
        };
        foreach (var image in BuildTiles(settings, platform))
            group.AddChild(image);
        group.AddChild(platform);
        platform.SurfaceBuiltKey = BuildKey(platform, settings);
        return group;
    }

    /// <summary>
    /// Generates the images of a platform's surface, in the platform's parent space, back to front (fill first).
    /// Requires the catalog (<see cref="EnsureCatalogAsync"/>); missing textures are skipped.
    /// </summary>
    public static List<ImageElement> BuildTiles(PlatformSurfaceSettings settings, PlatformElement platform)
    {
        var catalog = _catalog;
        double ox = Math.Round(platform.X), oy = Math.Round(platform.Y);
        double w = Math.Max(1, Math.Round(platform.Width)), h = Math.Max(1, Math.Round(platform.Height));
        var images = new List<ImageElement>();
        var rng = new Random(settings.Seed);
        bool floor = settings.Floor, leftWall = settings.LeftWall, rightWall = settings.RightWall;

        Sprite? Load(string name) => catalog.TryGetValue(name, out var s) ? s : null;

        List<Sprite>? Variants(string basePath)
        {
            var list = new List<Sprite>();
            for (int i = 1; i <= 3; i++)
                if (Load(basePath.Replace("_01", $"_{i:00}")) is { } s)
                    list.Add(s);
            return list.Count == 0 ? null : list;
        }

        Sprite Pick(List<Sprite> list) => list[rng.Next(list.Count)];

        void Add(Sprite sprite, double x, double y, double? width = null, double? height = null)
        {
            var image = new ImageElement
            {
                Name = sprite.Name,
                X = Math.Round(ox + x),
                Y = Math.Round(oy + y),
                Width = width ?? sprite.Width,
                Height = height ?? sprite.Height,
                NativeWidth = (int)sprite.Width,
                NativeHeight = (int)sprite.Height,
                SurfaceOwner = platform.Id,
            };
            image.AutoSize = width == null && height == null;
            images.Add(image);
        }

        void Fill(double left, double top, double right, double bottom)
        {
            double fw = right - left, fh = bottom - top;
            if (fw <= Epsilon || fh <= Epsilon)
                return;
            if (Load(BlackFill) is { } fill)
                Add(fill, left, top, Math.Round(fw), Math.Round(fh));
        }

        // Level space relative to the platform: left, top (Y down), right, bottom.
        double L = 0, T = 0, R = w, B = h;
        var behind = new List<ImageElement>();

        if (settings.Type == PlatformSurfaceType.Block)
        {
            Fill(L, T, R, B);
            return images;
        }

        bool smooth = settings.Type == PlatformSurfaceType.Smooth;
        // Rough: neighbouring tiles overlap by 1 pixel (no seams), and v_black grows 1 pixel on every side.
        int overlap = smooth ? 0 : 1;
        bool wantUpLeft = smooth || floor || leftWall;
        bool wantUpRight = smooth || floor || rightWall;
        var upLeft = wantUpLeft ? (smooth ? Load(CornerUpL) : Variants(CornerUpL) is { } ul ? Pick(ul) : null) : null;
        var upRight = wantUpRight ? (smooth ? Load(CornerUpR) : Variants(CornerUpR) is { } ur ? Pick(ur) : null) : null;

        if (upLeft != null) Add(upLeft, L, T);
        if (upRight != null) Add(upRight, R - upRight.Width, T);

        // Floor (Rough only): long tiles while they fit, short ones after, the last one snapped to the end.
        if (!smooth && floor && upLeft != null && upRight != null)
        {
            var shorts = Variants(Floor);
            var longs = Variants(FloorLong);
            if (shorts != null && longs != null)
            {
                double start = L + upLeft.Width - overlap, end = R - upRight.Width + overlap, x = start;
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
                    x += chosen.Width - overlap;
                }
            }
        }

        void Wall(bool right, Sprite upCorner)
        {
            var downs = Variants(right ? CornerDownR : CornerDownL);
            if (downs == null)
                return;
            var down = Pick(downs);
            double downTop = B - down.Height; // top-left of the bottom corner
            Add(down, right ? R - down.Width : L, downTop);
            var shorts = Variants(right ? WallR : WallL);
            var longs = Variants(right ? WallRLong : WallLLong);
            if (shorts == null || longs == null)
                return;
            double y = T + upCorner.Height - overlap, bottom = downTop + overlap;
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
                y += chosen.Height - overlap;
            }
        }

        if (leftWall && upLeft != null) Wall(false, upLeft);
        if (rightWall && upRight != null) Wall(true, upRight);

        // The fill goes behind the edges.
        int edges = images.Count;
        if (smooth)
        {
            Fill(L + SmoothFillInset.X, T + SmoothFillInset.Y, R - SmoothFillInset.X, B);
        }
        else
        {
            double left = leftWall && upLeft != null ? L + upLeft.Width : L;
            double right = rightWall && upRight != null ? R - upRight.Width : R;
            double top = floor && upLeft != null ? T + upLeft.Height : T;
            Fill(left - overlap, top - overlap, right + overlap, B + overlap);
        }
        if (images.Count > edges)
        {
            behind.AddRange(images.GetRange(edges, images.Count - edges));
            images.RemoveRange(edges, images.Count - edges);
        }
        behind.AddRange(images);
        return behind;
    }
}
