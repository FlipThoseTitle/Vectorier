// The game reads its content straight from the disk. In the editor every file it needs is read beforehand through
// the editor's file system (which also works in the browser) and kept here, under the project-relative paths the
// game builds ("xmlroot/levels/X.xml", "animations/data/run.bin", "sounds/step1.wav"). Textures are the editor's
// own bitmaps (its texture library), so nothing is decoded twice.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Vectorier.Gameplay;

public static class GameFiles
{
    private static readonly object Lock = new();
    private static readonly Dictionary<string, byte[]?> Files = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, SortedSet<string>> Folders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>"C:\x\..", "textures\a.png" → "textures/a.png" (the project root is "").</summary>
    public static string Key(string? path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        var key = path.Replace('\\', '/');
        while (key.Contains("//")) key = key.Replace("//", "/");
        if (key.StartsWith("./")) key = key[2..];
        return key.Trim('/');
    }

    private static string Parent(string key)
    {
        int i = key.LastIndexOf('/');
        return i < 0 ? string.Empty : key[..i];
    }

    public static void Clear()
    {
        lock (Lock)
        {
            Files.Clear();
            Folders.Clear();
        }
    }

    /// <summary>A file with its content (null: it exists, its content is not needed by the game).</summary>
    public static void Set(string path, byte[]? data)
    {
        string key = Key(path);
        lock (Lock)
        {
            Files[key] = data;
            // Every folder above it exists and lists it.
            string child = key;
            for (string dir = Parent(key); ; dir = Parent(dir))
            {
                if (!Folders.TryGetValue(dir, out var entries))
                    Folders[dir] = entries = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                entries.Add(child);
                if (dir.Length == 0) break;
                child = dir;
            }
        }
    }

    public static bool Contains(string path)
    {
        lock (Lock) return Files.ContainsKey(Key(path));
    }

    public static bool HasContent(string path)
    {
        lock (Lock) return Files.TryGetValue(Key(path), out var data) && data != null;
    }

    // ================= System.IO.File / Directory, as the game uses them ================= //

    public static bool FileExists(string path) => Contains(path);

    public static byte[]? ReadAllBytes(string path)
    {
        lock (Lock) return Files.TryGetValue(Key(path), out var data) ? data : null;
    }

    /// <summary>Like File.ReadAllText: UTF-8 (a byte order mark is skipped). Null when missing.</summary>
    public static string? ReadAllText(string path)
    {
        var data = ReadAllBytes(path);
        if (data == null) return null;
        int start = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? 3 : 0;
        return Encoding.UTF8.GetString(data, start, data.Length - start);
    }

    public static bool DirectoryExists(string path)
    {
        lock (Lock) return Folders.ContainsKey(Key(path));
    }

    /// <summary>Like Directory.GetFiles (the files directly in the folder).</summary>
    public static string[] GetFiles(string path)
    {
        string key = Key(path);
        lock (Lock)
        {
            if (!Folders.TryGetValue(key, out var entries)) return Array.Empty<string>();
            return entries.Where(Files.ContainsKey).ToArray();
        }
    }

    /// <summary>Every file with its content (their keys).</summary>
    public static List<string> GetAllKeys()
    {
        lock (Lock) return Files.Where(f => f.Value != null).Select(f => f.Key).ToList();
    }

    // ================= TEXTURES (the editor's) ================= //

    /// <summary>The editor's bitmap of a texture by its name (textures/, animatedtextures/), null when not loaded.</summary>
    public static Func<string, Avalonia.Media.IImage?>? TextureProvider { get; set; }

    /// <summary>A texture the game asked for that was not loaded yet (the host loads it, then restarts the level).</summary>
    public static Action<string>? TextureMissing { get; set; }

    /// <summary>A music track the game asked for before it was read (the host reads it and starts it).</summary>
    public static Action<string>? MusicMissing { get; set; }
}
