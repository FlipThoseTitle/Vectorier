using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace Vectorier.Services.IO;

/// <summary>
/// File system backed by an <see cref="IStorageFolder"/> picked by the user. Used by the browser build,
/// where local files are reached through the File System Access API instead of System.IO.
/// </summary>
public sealed class StorageFolderFileSystem : IFileSystem
{
    private readonly IStorageFolder _root;
    private readonly ConcurrentDictionary<string, IStorageFolder> _folderCache = new(StringComparer.Ordinal);

    public StorageFolderFileSystem(IStorageFolder root)
    {
        _root = root;
        _folderCache[string.Empty] = root;
    }

    public string RootDisplayName => _root.Name;
    public bool SupportsNativePaths => false;

    private static string[] Split(string path) => VPath.Normalize(path).Split('/', StringSplitOptions.RemoveEmptyEntries);

    private async Task<IStorageFolder?> GetFolderAsync(string path, bool create = false)
    {
        path = VPath.Normalize(path);
        if (_folderCache.TryGetValue(path, out var cached))
            return cached;

        IStorageFolder current = _root;
        string currentPath = string.Empty;
        foreach (string part in Split(path))
        {
            currentPath = VPath.Combine(currentPath, part);
            if (_folderCache.TryGetValue(currentPath, out var known))
            {
                current = known;
                continue;
            }

            IStorageFolder? next = null;
            try { next = await current.GetFolderAsync(part); } catch { next = null; }

            if (next == null)
            {
                if (!create)
                    return null;
                next = await current.CreateFolderAsync(part);
                if (next == null)
                    return null;
            }

            _folderCache[currentPath] = next;
            current = next;
        }
        return current;
    }

    private async Task<IStorageFile?> GetFileAsync(string path)
    {
        var folder = await GetFolderAsync(VPath.GetDirectory(path));
        if (folder == null)
            return null;
        try { return await folder.GetFileAsync(VPath.GetFileName(path)); }
        catch { return null; }
    }

    private void InvalidateFolder(string path)
    {
        path = VPath.Normalize(path);
        foreach (var key in _folderCache.Keys.ToList())
        {
            if (key == path || key.StartsWith(path + "/", StringComparison.Ordinal))
                _folderCache.TryRemove(key, out _);
        }
        _folderCache[string.Empty] = _root;
    }

    public async Task<bool> FileExistsAsync(string path) => await GetFileAsync(path) != null;

    public async Task<bool> DirectoryExistsAsync(string path) => await GetFolderAsync(path) != null;

    public async Task<IReadOnlyList<string>> GetFilesAsync(string directory, bool recursive = false)
    {
        var result = new List<string>();
        await CollectFilesAsync(VPath.Normalize(directory), recursive, result);
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private async Task CollectFilesAsync(string directory, bool recursive, List<string> result)
    {
        var folder = await GetFolderAsync(directory);
        if (folder == null)
            return;

        var subFolders = new List<string>();
        await foreach (var item in folder.GetItemsAsync())
        {
            string itemPath = VPath.Combine(directory, item.Name);
            if (item is IStorageFile)
                result.Add(itemPath);
            else if (item is IStorageFolder sub)
            {
                _folderCache[itemPath] = sub;
                subFolders.Add(itemPath);
            }
        }

        if (recursive)
        {
            foreach (var sub in subFolders)
                await CollectFilesAsync(sub, true, result);
        }
    }

    public async Task<IReadOnlyList<string>> GetDirectoriesAsync(string directory)
    {
        directory = VPath.Normalize(directory);
        var folder = await GetFolderAsync(directory);
        var result = new List<string>();
        if (folder == null)
            return result;

        await foreach (var item in folder.GetItemsAsync())
        {
            if (item is IStorageFolder sub)
            {
                string itemPath = VPath.Combine(directory, item.Name);
                _folderCache[itemPath] = sub;
                result.Add(itemPath);
            }
        }
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    public async Task<byte[]> ReadAllBytesAsync(string path)
    {
        var file = await GetFileAsync(path) ?? throw new FileNotFoundException(path);
        await using var stream = await file.OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    public async Task<string> ReadAllTextAsync(string path)
    {
        var bytes = await ReadAllBytesAsync(path);
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true);
        return await reader.ReadToEndAsync();
    }

    public async Task WriteAllBytesAsync(string path, byte[] data)
    {
        var folder = await GetFolderAsync(VPath.GetDirectory(path), create: true)
                     ?? throw new IOException("Cannot create folder for " + path);
        string name = VPath.GetFileName(path);

        IStorageFile? file = null;
        try { file = await folder.GetFileAsync(name); } catch { file = null; }
        file ??= await folder.CreateFileAsync(name) ?? throw new IOException("Cannot create " + path);

        await using var stream = await file.OpenWriteAsync();
        stream.SetLength(0);
        await stream.WriteAsync(data);
        await stream.FlushAsync();
    }

    public Task WriteAllTextAsync(string path, string text) => WriteAllBytesAsync(path, new UTF8Encoding(false).GetBytes(text));

    public async Task<Stream> OpenReadAsync(string path)
    {
        // Copy to memory so callers can seek and dispose freely.
        return new MemoryStream(await ReadAllBytesAsync(path));
    }

    public async Task CreateDirectoryAsync(string path) => await GetFolderAsync(path, create: true);

    public async Task DeleteFileAsync(string path)
    {
        var file = await GetFileAsync(path);
        if (file != null)
            await file.DeleteAsync();
    }

    public async Task DeleteDirectoryAsync(string path)
    {
        var folder = await GetFolderAsync(path);
        if (folder != null)
            await folder.DeleteAsync();
        InvalidateFolder(path);
    }

    public async Task MoveFileAsync(string from, string to, bool overwrite = false)
    {
        if (await FileExistsAsync(to))
        {
            if (!overwrite)
                throw new IOException("File already exists: " + to);
            await DeleteFileAsync(to);
        }
        await WriteAllBytesAsync(to, await ReadAllBytesAsync(from));
        await DeleteFileAsync(from);
    }

    public async Task MoveDirectoryAsync(string from, string to)
    {
        await CopyDirectoryAsync(from, to);
        await DeleteDirectoryAsync(from);
    }

    public async Task CopyFileAsync(string from, string to, bool overwrite = true)
    {
        if (!overwrite && await FileExistsAsync(to))
            return;
        await WriteAllBytesAsync(to, await ReadAllBytesAsync(from));
    }

    public async Task CopyDirectoryAsync(string from, string to, IProgress<string>? progress = null)
    {
        var source = await GetFolderAsync(from) ?? throw new DirectoryNotFoundException(from);
        var target = await GetFolderAsync(to, create: true) ?? throw new IOException("Cannot create " + to);
        await FastCopy.CopyFolderAsync(source, target, progress);
        InvalidateFolder(to);
    }

    public async Task<DateTimeOffset?> GetLastWriteTimeAsync(string path)
    {
        IStorageItem? item = await GetFileAsync(path);
        item ??= await GetFolderAsync(path);
        if (item == null)
            return null;
        var props = await item.GetBasicPropertiesAsync();
        return props.DateModified;
    }

    public string? GetNativePath(string path) => null;

    public string? TryGetRelativePath(string nativePath) => null;

    public Task<IStorageFolder?> GetStorageFolderAsync(string path, IStorageProvider provider) => GetFolderAsync(path);
}
