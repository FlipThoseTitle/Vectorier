using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace Vectorier.Services.IO;

/// <summary>System.IO implementation used by the desktop builds.</summary>
public sealed class NativeFileSystem : IFileSystem
{
    private readonly string _root;

    public NativeFileSystem(string root)
    {
        _root = Path.GetFullPath(root);
    }

    public string RootPath => _root;
    public string RootDisplayName => _root;
    public bool SupportsNativePaths => true;

    private static readonly bool CaseSensitive = !OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS();

    private string Full(string path)
    {
        string normalized = VPath.Normalize(path);
        string full = normalized.Length == 0 ? _root : Path.Combine(_root, normalized.Replace('/', Path.DirectorySeparatorChar));
        return CaseSensitive ? ResolveCase(full) : full;
    }

    /// <summary>A move target: its folder is matched ignoring case, its own name is kept exactly (case-only renames).</summary>
    private string FullTarget(string path)
    {
        string normalized = VPath.Normalize(path);
        string dir = Full(VPath.GetDirectory(normalized));
        return normalized.Length == 0 ? _root : Path.Combine(dir, VPath.GetFileName(normalized));
    }

    /// <summary>
    /// Linux file systems are case-sensitive, while Vector's XML (made on Windows) may name "Buildings.xml" for
    /// buildings.xml. A path that does not exist as written is matched segment by segment ignoring case; the parts
    /// that do not exist yet (new files and folders) are kept as written.
    /// </summary>
    private string ResolveCase(string full)
    {
        if (File.Exists(full) || Directory.Exists(full) || !full.StartsWith(_root, StringComparison.Ordinal))
            return full;
        string current = _root;
        var parts = full[_root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string exact = Path.Combine(current, parts[i]);
            if (File.Exists(exact) || Directory.Exists(exact) || !Directory.Exists(current))
            {
                current = exact;
                continue;
            }
            string? match = null;
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(current))
                {
                    if (string.Equals(Path.GetFileName(entry), parts[i], StringComparison.OrdinalIgnoreCase))
                    {
                        match = entry;
                        break;
                    }
                }
            }
            catch
            {
                // unreadable folder: keep the name as written
            }
            current = match ?? exact;
        }
        return current;
    }

    private string Relative(string full) => VPath.Normalize(Path.GetRelativePath(_root, full));

    public Task<bool> FileExistsAsync(string path) => Task.FromResult(File.Exists(Full(path)));

    public Task<bool> DirectoryExistsAsync(string path) => Task.FromResult(Directory.Exists(Full(path)));

    public Task<IReadOnlyList<string>> GetFilesAsync(string directory, bool recursive = false)
    {
        string full = Full(directory);
        if (!Directory.Exists(full))
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        var files = Directory.EnumerateFiles(full, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Select(Relative)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(files);
    }

    public Task<IReadOnlyList<string>> GetDirectoriesAsync(string directory)
    {
        string full = Full(directory);
        if (!Directory.Exists(full))
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        var dirs = Directory.EnumerateDirectories(full)
            .Select(Relative)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(dirs);
    }

    public Task<byte[]> ReadAllBytesAsync(string path) => File.ReadAllBytesAsync(Full(path));

    public Task<string> ReadAllTextAsync(string path) => File.ReadAllTextAsync(Full(path));

    public async Task WriteAllBytesAsync(string path, byte[] data)
    {
        string full = Full(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, data);
    }

    public async Task WriteAllTextAsync(string path, string text)
    {
        string full = Full(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, text);
    }

    public Task<Stream> OpenReadAsync(string path) => Task.FromResult<Stream>(File.OpenRead(Full(path)));

    public Task CreateDirectoryAsync(string path)
    {
        Directory.CreateDirectory(Full(path));
        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(string path)
    {
        string full = Full(path);
        if (File.Exists(full))
            File.Delete(full);
        return Task.CompletedTask;
    }

    public Task DeleteDirectoryAsync(string path)
    {
        string full = Full(path);
        if (Directory.Exists(full))
            Directory.Delete(full, true);
        return Task.CompletedTask;
    }

    public Task MoveFileAsync(string from, string to, bool overwrite = false)
    {
        string target = FullTarget(to);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(Full(from), target, overwrite);
        return Task.CompletedTask;
    }

    public Task MoveDirectoryAsync(string from, string to)
    {
        string source = Full(from);
        string target = FullTarget(to);

        // Case-only renames need an intermediate step on case-insensitive file systems.
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase) && !string.Equals(source, target, StringComparison.Ordinal))
        {
            string temp = source + "_" + Guid.NewGuid().ToString("N")[..8];
            Directory.Move(source, temp);
            Directory.Move(temp, target);
        }
        else
        {
            Directory.Move(source, target);
        }
        return Task.CompletedTask;
    }

    public Task CopyFileAsync(string from, string to, bool overwrite = true)
    {
        string target = Full(to);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(Full(from), target, overwrite);
        return Task.CompletedTask;
    }

    public Task CopyDirectoryAsync(string from, string to, IProgress<string>? progress = null)
    {
        return Task.Run(() => FastCopy.CopyDirectory(Full(from), Full(to), progress));
    }

    public Task<DateTimeOffset?> GetLastWriteTimeAsync(string path)
    {
        string full = Full(path);
        if (File.Exists(full))
            return Task.FromResult<DateTimeOffset?>(File.GetLastWriteTimeUtc(full));
        if (Directory.Exists(full))
            return Task.FromResult<DateTimeOffset?>(Directory.GetLastWriteTimeUtc(full));
        return Task.FromResult<DateTimeOffset?>(null);
    }

    public string? GetNativePath(string path) => Full(path);

    public string? TryGetRelativePath(string nativePath)
    {
        string full = Path.GetFullPath(nativePath);
        string relative = Path.GetRelativePath(_root, full);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            return null;
        return VPath.Normalize(relative);
    }

    public async Task<IStorageFolder?> GetStorageFolderAsync(string path, IStorageProvider provider)
    {
        string full = Full(path);
        if (!Directory.Exists(full))
            return null;
        try
        {
            return await provider.TryGetFolderFromPathAsync(new Uri(full));
        }
        catch
        {
            return null;
        }
    }
}
