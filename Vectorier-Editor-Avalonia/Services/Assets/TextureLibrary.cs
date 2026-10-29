using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Vectorier_Editor_Avalonia.Services.IO;
using Vectorier_Editor_Avalonia.Services.Projects;

namespace Vectorier_Editor_Avalonia.Services.Assets;

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

/// <summary>
/// Index and cache of a project's textures (textures/ and animatedtextures/). Bitmaps load asynchronously;
/// <see cref="TextureLoaded"/> fires on the UI thread when one becomes available.
/// </summary>
public sealed class TextureLibrary
{
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg" };

    /// <summary>Game textures hidden from the browsers (Unity's exclusion list). The parrots stay visible.</summary>
    public static readonly HashSet<string> ExcludedTextures = new(StringComparer.Ordinal)
    {
        "bonus_num", "circle", "GStick_arrow", "GStick_circle", "v_back", "tap", "rect"
    };

    public static readonly HashSet<string> ExcludedAnimatedTextures = new(StringComparer.Ordinal)
    {
        "antibot", "bonus_v4", "bonus_v4_off", "credits", "glass_1", "credits_off",
        "lightning_expl_v2", "lightning_hands", "lightning_paraliz_v2", "paper_v1",
        "reverse_indicator_left", "reverse_indicator_right", "run_indicator", "stopsign",
        "trick_active_up", "trick_idle_up", "bird_v0", "bird_v2", "bird_v3"
    };

    /// <summary>Textures that must not be deleted (the excluded ones plus the parrots).</summary>
    public static bool IsProtected(TextureEntry entry) =>
        (entry.IsAnimated ? ExcludedAnimatedTextures : ExcludedTextures).Contains(entry.Name) ||
        entry.Name is "parrot01" or "parrot02" or "parrot03";

    private readonly IFileSystem _fs;
    private readonly ProjectInfo _project;
    private readonly Dictionary<string, TextureEntry> _byName = new(StringComparer.Ordinal);
    private readonly List<TextureEntry> _all = new();
    private readonly ConcurrentDictionary<string, TextureAsset?> _loaded = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task<TextureAsset?>> _loading = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Bitmap?> _thumbs = new(StringComparer.Ordinal);

    public TextureLibrary(IFileSystem fs, ProjectInfo project)
    {
        _fs = fs;
        _project = project;
    }

    public event Action<string>? TextureLoaded;

    public IReadOnlyList<TextureEntry> Entries => _all;

    /// <summary>
    /// Entries shown in the Editor's texture browser: every animated texture (.plist; levels use the game's own ones
    /// too) and the static textures except the game's UI ones.
    /// </summary>
    public IEnumerable<TextureEntry> BrowsableEntries =>
        _all.Where(e => e.IsAnimated || !ExcludedTextures.Contains(e.Name));

    public bool IsIndexed { get; private set; }

    public async Task RefreshIndexAsync()
    {
        var entries = new List<TextureEntry>();

        // Sprite sheets first: they win over a static texture with the same name.
        var animatedFiles = await _fs.GetFilesAsync(_project.AnimatedTexturesFolder);
        var fileSet = animatedFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in animatedFiles)
        {
            if (!VPath.HasExtension(file, ImageExtensions))
                continue;
            string plist = VPath.ChangeExtension(file, ".plist");
            string json = VPath.ChangeExtension(file, ".json");
            string? atlas = fileSet.Contains(plist) ? plist : fileSet.Contains(json) ? json : null;
            entries.Add(new TextureEntry(VPath.GetFileNameWithoutExtension(file), file, atlas));
        }

        foreach (var file in await _fs.GetFilesAsync(_project.TexturesFolder))
        {
            if (VPath.HasExtension(file, ImageExtensions))
                entries.Add(new TextureEntry(VPath.GetFileNameWithoutExtension(file), file, null));
        }

        _all.Clear();
        _byName.Clear();
        foreach (var entry in entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            _all.Add(entry);
            // Lookup priority: animated, then .png, .jpg, .jpeg.
            if (!_byName.TryGetValue(entry.Name, out var existing) || Priority(entry) < Priority(existing))
                _byName[entry.Name] = entry;
        }
        IsIndexed = true;
    }

    private static int Priority(TextureEntry entry)
    {
        if (entry.IsAnimated) return 0;
        return entry.Extension.ToLowerInvariant() switch { ".png" => 1, ".jpg" => 2, _ => 3 };
    }

    public TextureEntry? Find(string? name) => name != null && _byName.TryGetValue(name, out var entry) ? entry : null;

    /// <summary>Returns the texture if it is loaded; otherwise starts loading it and returns null.</summary>
    public TextureAsset? TryGet(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        if (_loaded.TryGetValue(name, out var asset))
            return asset;
        _ = GetAsync(name);
        return null;
    }

    public bool IsMissing(string? name) => IsIndexed && Find(name) == null;

    public Task<TextureAsset?> GetAsync(string name)
    {
        if (_loaded.TryGetValue(name, out var asset))
            return Task.FromResult(asset);
        return _loading.GetOrAdd(name, LoadAsync);
    }

    private async Task<TextureAsset?> LoadAsync(string name)
    {
        TextureAsset? asset = null;
        try
        {
            var entry = Find(name);
            if (entry != null)
            {
                byte[] data = await _fs.ReadAllBytesAsync(entry.Path);
                string? atlasText = entry.AtlasPath != null ? await _fs.ReadAllTextAsync(entry.AtlasPath) : null;
                asset = await Task.Run(() =>
                {
                    var bitmap = ImageLoader.Decode(data);
                    if (bitmap == null)
                        return null;
                    IReadOnlyList<AtlasFrame>? frames = null;
                    if (atlasText != null)
                    {
                        try { frames = AtlasParser.Parse(atlasText); }
                        catch { frames = null; }
                    }
                    return new TextureAsset(entry, bitmap, frames);
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TextureLibrary] {name}: {ex.Message}");
        }

        _loaded[name] = asset;
        _loading.TryRemove(name, out _);
        Dispatcher.UIThread.Post(() => TextureLoaded?.Invoke(name));
        return asset;
    }

    /// <summary>Loads one specific file (not cached by name; used for duplicates hidden behind another file).</summary>
    public async Task<TextureAsset?> LoadEntryAsync(TextureEntry entry)
    {
        try
        {
            byte[] data = await _fs.ReadAllBytesAsync(entry.Path);
            string? atlasText = entry.AtlasPath != null ? await _fs.ReadAllTextAsync(entry.AtlasPath) : null;
            return await Task.Run(() =>
            {
                var bitmap = ImageLoader.Decode(data);
                if (bitmap == null) return null;
                IReadOnlyList<AtlasFrame>? frames = null;
                if (atlasText != null)
                {
                    try { frames = AtlasParser.Parse(atlasText); }
                    catch { frames = null; }
                }
                return new TextureAsset(entry, bitmap, frames);
            });
        }
        catch
        {
            return null;
        }
    }

    private readonly ConcurrentDictionary<string, Task<Bitmap?>> _thumbLoading = new(StringComparer.Ordinal);

    /// <summary>
    /// Downscaled bitmap of a static texture for small previews, or null while it loads (then
    /// <see cref="TextureLoaded"/> fires). <paramref name="width"/> is rounded up to a power of two.
    /// </summary>
    public Bitmap? TryGetThumbnail(TextureEntry entry, int width)
    {
        width = Math.Clamp((int)Math.Pow(2, Math.Ceiling(Math.Log2(Math.Max(16, width)))), 16, 4096);
        string key = entry.Path + "|" + width;
        if (_thumbs.TryGetValue(key, out var cached))
            return cached;
        _thumbLoading.GetOrAdd(key, async _ =>
        {
            Bitmap? bitmap = null;
            try
            {
                byte[] data = await _fs.ReadAllBytesAsync(entry.Path);
                bitmap = await Task.Run(() =>
                {
                    int? original = PngWidth(data);
                    return ImageLoader.Decode(data, original != null && original <= width ? null : width);
                });
            }
            catch
            {
                bitmap = null;
            }
            _thumbs[key] = bitmap;
            _thumbLoading.TryRemove(key, out Task<Bitmap?>? _);
            Dispatcher.UIThread.Post(() => TextureLoaded?.Invoke(entry.Name));
            return bitmap;
        });
        return null;
    }

    private static int? PngWidth(byte[] data) =>
        data.Length > 24 && data[0] == 0x89 && data[1] == (byte)'P' && data[2] == (byte)'N' && data[3] == (byte)'G'
            ? (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19]
            : null;

    /// <summary>Small preview bitmap for grids (static textures decode downscaled; sprite sheets use the full sheet).</summary>
    public async Task<Bitmap?> GetThumbnailAsync(TextureEntry entry, int width = 128)
    {
        if (entry.IsAnimated)
            return (await GetAsync(entry.Name))?.Bitmap;
        string key = entry.Path + "|" + width;
        if (_thumbs.TryGetValue(key, out var cached))
            return cached;
        // Tiles showing the same file share one decode.
        return await _thumbLoading.GetOrAdd("g|" + key, async _ =>
        {
            var bitmap = await ImageLoader.LoadAsync(_fs, entry.Path, width);
            _thumbs[key] = bitmap;
            _thumbLoading.TryRemove("g|" + key, out Task<Bitmap?>? _);
            return bitmap;
        });
    }

    public void Invalidate(string name)
    {
        _loaded.TryRemove(name, out _);
        foreach (var key in _thumbs.Keys.Where(k => k.Contains("/" + name + ".", StringComparison.Ordinal)).ToList())
            _thumbs.TryRemove(key, out _);
    }

    // ================= IMPORT / DELETE ================= //

    public static bool IsImageFile(string name) => VPath.HasExtension(name, ImageExtensions);

    /// <summary>
    /// Imports picked image files. An image with a matching .plist/.json in the same batch is a sprite sheet
    /// and goes to animatedtextures; other images go to textures. Returns the imported texture names.
    /// </summary>
    public async Task<List<string>> ImportAsync(IReadOnlyList<(string FileName, Func<Task<byte[]>> Read)> files, Func<string, Task<bool>>? confirmOverwrite = null)
    {
        var imported = new List<string>();
        var byName = files.ToDictionary(f => f.FileName, f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var (fileName, read) in files)
        {
            if (!IsImageFile(fileName))
                continue;

            string stem = VPath.GetFileNameWithoutExtension(fileName);
            (string, Func<Task<byte[]>>)? atlas = null;
            if (byName.TryGetValue(stem + ".plist", out var plist)) atlas = plist;
            else if (byName.TryGetValue(stem + ".json", out var json)) atlas = json;

            string folder = atlas != null ? _project.AnimatedTexturesFolder : _project.TexturesFolder;
            string target = VPath.Combine(folder, fileName);
            if (await _fs.FileExistsAsync(target) && confirmOverwrite != null && !await confirmOverwrite(fileName))
                continue;

            await _fs.WriteAllBytesAsync(target, await read());
            if (atlas is { } a)
                await _fs.WriteAllBytesAsync(VPath.Combine(folder, a.Item1), await a.Item2());

            Invalidate(stem);
            imported.Add(stem);
        }

        await RefreshIndexAsync();
        return imported;
    }

    public async Task DeleteAsync(TextureEntry entry)
    {
        await _fs.DeleteFileAsync(entry.Path);
        if (entry.AtlasPath != null)
            await _fs.DeleteFileAsync(entry.AtlasPath);
        Invalidate(entry.Name);
        await RefreshIndexAsync();
    }

    /// <summary>Renames a texture file (and its atlas). Levels referencing the old name are not changed.</summary>
    public async Task RenameAsync(TextureEntry entry, string newName)
    {
        string folder = VPath.GetDirectory(entry.Path);
        string target = VPath.Combine(folder, newName + entry.Extension);
        if (await _fs.FileExistsAsync(target))
            throw new InvalidOperationException($"'{newName}{entry.Extension}' already exists.");
        await _fs.MoveFileAsync(entry.Path, target);
        if (entry.AtlasPath != null)
            await _fs.MoveFileAsync(entry.AtlasPath, VPath.Combine(folder, newName + VPath.GetExtension(entry.AtlasPath)));
        Invalidate(entry.Name);
        await RefreshIndexAsync();
    }
}
