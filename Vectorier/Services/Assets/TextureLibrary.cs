using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Vectorier.Services.IO;
using Vectorier.Services.Localization;
using Vectorier.Services.Projects;

namespace Vectorier.Services.Assets;

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

    /// <summary>Pixel size of texture files read from their header (by path), so sizes never need a full decode.</summary>
    private readonly ConcurrentDictionary<string, (int W, int H)> _sizes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task> _sizeLoading = new(StringComparer.Ordinal);

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

    /// <summary>The texture if it is already loaded (never starts a load).</summary>
    public TextureAsset? TryGetLoaded(string? name) =>
        !string.IsNullOrEmpty(name) && _loaded.TryGetValue(name, out var asset) ? asset : null;

    /// <summary>
    /// Pixel size of a texture without decoding it: the loaded texture's, or the file header's for static textures
    /// (read once; <see cref="TextureLoaded"/> fires when known). Sprite sheets need their atlas, so they load.
    /// </summary>
    public (int Width, int Height)? TryGetSize(TextureEntry entry)
    {
        if (ReferenceEquals(Find(entry.Name), entry) && _loaded.TryGetValue(entry.Name, out var loaded) && loaded != null)
            return (loaded.Width, loaded.Height);
        if (entry.IsAnimated)
            return TryGet(entry.Name) is { } asset ? (asset.Width, asset.Height) : null;
        if (_sizes.TryGetValue(entry.Path, out var size))
            return size;
        _sizeLoading.GetOrAdd(entry.Path, static (_, arg) => arg.Library.ReadSizeAsync(arg.Entry), (Library: this, Entry: entry));
        return null;
    }

    public (int Width, int Height)? TryGetSize(string? name) => Find(name) is { } entry ? TryGetSize(entry) : null;

    private async Task ReadSizeAsync(TextureEntry entry)
    {
        try
        {
            await using var stream = await _fs.OpenReadAsync(entry.Path);
            // PNG keeps its size in the first bytes; JPEG in a frame header after the metadata.
            var buffer = new byte[256 * 1024];
            int read = 0, n;
            while (read < buffer.Length && (n = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read))) > 0)
                read += n;
            if (ImageHeader.TryReadSize(buffer.AsSpan(0, read), out int w, out int h))
                _sizes[entry.Path] = (w, h);
            else if (await Task.Run(async () => ImageLoader.Decode(await _fs.ReadAllBytesAsync(entry.Path))) is { } bitmap)
            {
                // Unusual file: decode once for its size only.
                using (bitmap)
                    _sizes[entry.Path] = (bitmap.PixelSize.Width, bitmap.PixelSize.Height);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TextureLibrary] size of {entry.Name}: {ex.Message}");
        }
        _sizeLoading.TryRemove(entry.Path, out _);
        Dispatcher.UIThread.Post(() => TextureLoaded?.Invoke(entry.Name));
    }

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
                if (asset != null && !entry.IsAnimated)
                    _sizes[entry.Path] = (asset.Bitmap.PixelSize.Width, asset.Bitmap.PixelSize.Height);
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

    // ================= THUMBNAILS (downscaled copies) ================= //

    /// <summary>
    /// Memory the downscaled copies (Project Window, Project Manager, zoomed-out Scene) may keep. Past it the least
    /// recently drawn ones are freed and decoded again from the file when needed. Every user draws them at render
    /// time only (nothing holds them), so freeing one never breaks a view.
    /// </summary>
    public static long ThumbnailBudgetBytes { get; set; } = 160L * 1024 * 1024;

    private sealed class Thumb
    {
        public Thumb(Bitmap? bitmap)
        {
            Bitmap = bitmap;
            Bytes = bitmap == null ? 64 : (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4;
        }

        public Bitmap? Bitmap { get; }
        public long Bytes { get; }
        public long LastUse;
    }

    // UI thread only (renders and the posted completions).
    // Keyed by (texture path, width): looked up for every image drawn, so the key is not a built string.
    private readonly Dictionary<(string Path, int Width), Thumb> _thumbs = new();
    private readonly HashSet<(string Path, int Width)> _thumbLoading = new();
    private long _thumbBytes;

    /// <summary>Thumbnails drawn this recently are never freed (what is on screen stays, even past the budget).</summary>
    private const long RecentUseMs = 3000;

    public const int MinThumbnailWidth = 16;
    public const int MaxThumbnailWidth = 4096;

    /// <summary>Decode width for an on-screen width: the next power of two (so a zoom only re-decodes at each doubling).</summary>
    public static int ThumbnailWidth(double pixelWidth) =>
        Math.Clamp((int)Math.Pow(2, Math.Ceiling(Math.Log2(Math.Max(MinThumbnailWidth, pixelWidth)))), MinThumbnailWidth, MaxThumbnailWidth);

    private static (string Path, int Width) ThumbKey(TextureEntry entry, int width) => (entry.Path, width);

    /// <summary>
    /// Downscaled bitmap of a static texture for an on-screen width (rounded up to a power of two), or null while it
    /// loads (then <see cref="TextureLoaded"/> fires). With <paramref name="nearest"/>, another size already in memory
    /// is returned meanwhile, so zooming does not blink. Draw it right away; do not keep it.
    /// </summary>
    public Bitmap? TryGetThumbnail(TextureEntry entry, int width, bool nearest = false)
    {
        width = ThumbnailWidth(width);
        var key = ThumbKey(entry, width);
        if (_thumbs.TryGetValue(key, out var cached))
        {
            cached.LastUse = Environment.TickCount64;
            return cached.Bitmap;
        }
        if (_thumbLoading.Add(key))
            _ = LoadThumbnailAsync(entry, width, key);
        if (!nearest)
            return null;
        // Closest size in memory: larger first (sharper), then smaller.
        for (int w = width * 2; w <= MaxThumbnailWidth; w *= 2)
            if (_thumbs.TryGetValue(ThumbKey(entry, w), out var larger) && larger.Bitmap != null)
                return Touch(larger);
        for (int w = width / 2; w >= MinThumbnailWidth; w /= 2)
            if (_thumbs.TryGetValue(ThumbKey(entry, w), out var smaller) && smaller.Bitmap != null)
                return Touch(smaller);
        return null;
    }

    private Bitmap? Touch(Thumb thumb)
    {
        thumb.LastUse = Environment.TickCount64;
        return thumb.Bitmap;
    }

    private async Task LoadThumbnailAsync(TextureEntry entry, int width, (string Path, int Width) key)
    {
        Bitmap? bitmap = null;
        try
        {
            byte[] data = await _fs.ReadAllBytesAsync(entry.Path);
            bitmap = await Task.Run(() =>
            {
                bool known = ImageHeader.TryReadSize(data, out int w, out int h);
                if (known)
                    _sizes[entry.Path] = (w, h);
                // Never upscale: a file smaller than the width is decoded as it is.
                return ImageLoader.Decode(data, known && w <= width ? null : width);
            });
        }
        catch
        {
            bitmap = null;
        }
        Dispatcher.UIThread.Post(() =>
        {
            _thumbLoading.Remove(key);
            if (_thumbs.Remove(key, out var old))
            {
                _thumbBytes -= old.Bytes;
                old.Bitmap?.Dispose();
            }
            var thumb = new Thumb(bitmap) { LastUse = Environment.TickCount64 };
            _thumbs[key] = thumb;
            _thumbBytes += thumb.Bytes;
            TrimThumbnails();
            TextureLoaded?.Invoke(entry.Name);
        });
    }

    /// <summary>Frees the least recently drawn thumbnails until the cache is back under 90% of its budget.</summary>
    private void TrimThumbnails()
    {
        if (_thumbBytes <= ThumbnailBudgetBytes)
            return;
        long target = ThumbnailBudgetBytes * 9 / 10;
        long recent = Environment.TickCount64 - RecentUseMs;
        foreach (var (key, thumb) in _thumbs.OrderBy(t => t.Value.LastUse).ToList())
        {
            if (_thumbBytes <= target || thumb.LastUse >= recent)
                break;
            _thumbs.Remove(key);
            _thumbBytes -= thumb.Bytes;
            thumb.Bitmap?.Dispose();
        }
    }

    // ================= RELEASING ================= //

    /// <summary>
    /// Lets go of the full-size textures (a level editor closed): they load again when something draws them. Every
    /// user draws them at render time only, so none is left holding one.
    /// </summary>
    public void ReleaseLoaded()
    {
        foreach (var asset in _loaded.Values)
            asset?.Bitmap.Dispose();
        _loaded.Clear();
    }

    /// <summary>Lets go of everything loaded (another project is opened). UI thread.</summary>
    public void ReleaseAll()
    {
        ReleaseLoaded();
        foreach (var thumb in _thumbs.Values)
            thumb.Bitmap?.Dispose();
        _thumbs.Clear();
        _thumbBytes = 0;
        _sizes.Clear();
    }

    public void Invalidate(string name)
    {
        _loaded.TryRemove(name, out _);
        foreach (var path in _sizes.Keys.Where(k => k.Contains("/" + name + ".", StringComparison.Ordinal)).ToList())
            _sizes.TryRemove(path, out _);
        void Drop()
        {
            foreach (var key in _thumbs.Keys.Where(k => k.Path.Contains("/" + name + ".", StringComparison.Ordinal)).ToList())
            {
                if (_thumbs.Remove(key, out var thumb))
                {
                    _thumbBytes -= thumb.Bytes;
                    thumb.Bitmap?.Dispose();
                }
            }
        }
        if (Dispatcher.UIThread.CheckAccess()) Drop();
        else Dispatcher.UIThread.Post(Drop);
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
            throw new InvalidOperationException(Loc.F("'{0}{1}' already exists.", newName, entry.Extension));
        await _fs.MoveFileAsync(entry.Path, target);
        if (entry.AtlasPath != null)
            await _fs.MoveFileAsync(entry.AtlasPath, VPath.Combine(folder, newName + VPath.GetExtension(entry.AtlasPath)));
        Invalidate(entry.Name);
        await RefreshIndexAsync();
    }
}
