using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace Vectorier.Services.IO;

/// <summary>
/// File access rooted at the Vectorier application folder (the folder that holds Editor, Game and Projects).
/// All paths are relative to that root and use '/' as the separator, so the same code works on the
/// desktop (System.IO) and in the browser (File System Access API through Avalonia's storage provider).
/// </summary>
public interface IFileSystem
{
    /// <summary>Human readable description of the root (a native path on desktop, a folder name in the browser).</summary>
    string RootDisplayName { get; }

    /// <summary>True when files can be addressed by native OS paths (desktop).</summary>
    bool SupportsNativePaths { get; }

    Task<bool> FileExistsAsync(string path);
    Task<bool> DirectoryExistsAsync(string path);

    /// <summary>Returns the relative paths of the files directly inside (or below, when recursive) a directory.</summary>
    Task<IReadOnlyList<string>> GetFilesAsync(string directory, bool recursive = false);

    /// <summary>Returns the relative paths of the sub-directories directly inside a directory.</summary>
    Task<IReadOnlyList<string>> GetDirectoriesAsync(string directory);

    Task<byte[]> ReadAllBytesAsync(string path);
    Task<string> ReadAllTextAsync(string path);
    Task WriteAllBytesAsync(string path, byte[] data);
    Task WriteAllTextAsync(string path, string text);
    Task<Stream> OpenReadAsync(string path);

    Task CreateDirectoryAsync(string path);
    Task DeleteFileAsync(string path);
    Task DeleteDirectoryAsync(string path);
    Task MoveFileAsync(string from, string to, bool overwrite = false);
    Task MoveDirectoryAsync(string from, string to);
    Task CopyFileAsync(string from, string to, bool overwrite = true);
    Task CopyDirectoryAsync(string from, string to, IProgress<string>? progress = null);

    Task<DateTimeOffset?> GetLastWriteTimeAsync(string path);

    /// <summary>Native absolute path for a relative path, or null when the platform has no native paths.</summary>
    string? GetNativePath(string path);

    /// <summary>Maps a native path back to a relative path when it lies inside the root.</summary>
    string? TryGetRelativePath(string nativePath);

    /// <summary>Storage folder for a relative directory, used as the start location of pickers.</summary>
    Task<IStorageFolder?> GetStorageFolderAsync(string path, IStorageProvider provider);
}

public static class VPath
{
    public static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        path = path.Replace('\\', '/');
        while (path.Contains("//"))
            path = path.Replace("//", "/");
        return path.Trim('/');
    }

    public static string Combine(params string[] parts)
    {
        var list = new List<string>();
        foreach (var part in parts)
        {
            var normalized = Normalize(part);
            if (normalized.Length > 0)
                list.Add(normalized);
        }
        return string.Join('/', list);
    }

    public static string GetFileName(string path)
    {
        path = Normalize(path);
        int index = path.LastIndexOf('/');
        return index < 0 ? path : path[(index + 1)..];
    }

    public static string GetFileNameWithoutExtension(string path)
    {
        string name = GetFileName(path);
        int dot = name.LastIndexOf('.');
        return dot <= 0 ? name : name[..dot];
    }

    public static string GetExtension(string path)
    {
        string name = GetFileName(path);
        int dot = name.LastIndexOf('.');
        return dot < 0 ? string.Empty : name[dot..];
    }

    public static string GetDirectory(string path)
    {
        path = Normalize(path);
        int index = path.LastIndexOf('/');
        return index < 0 ? string.Empty : path[..index];
    }

    public static string ChangeExtension(string path, string extension)
    {
        string dir = GetDirectory(path);
        string name = GetFileNameWithoutExtension(path) + extension;
        return Combine(dir, name);
    }

    public static bool HasExtension(string path, params string[] extensions)
    {
        string ext = GetExtension(path);
        foreach (var e in extensions)
        {
            if (string.Equals(ext, e, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
