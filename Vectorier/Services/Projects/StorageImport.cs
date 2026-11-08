using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Vectorier.Services.IO;

namespace Vectorier.Services.Projects;

/// <summary>Copies picked files and folders (from pickers or drag and drop) into the Vectorier file system.</summary>
public static class StorageImport
{
    public static async Task<byte[]> ReadAllBytesAsync(IStorageFile file)
    {
        await using var stream = await file.OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    public static async Task CopyFileAsync(IFileSystem fs, IStorageFile file, string targetPath)
    {
        await fs.WriteAllBytesAsync(targetPath, await ReadAllBytesAsync(file));
    }

    public static async Task CopyFolderAsync(IFileSystem fs, IStorageFolder folder, string targetPath, IProgress<string>? progress = null)
    {
        // Desktop: both ends are local folders, so copy them natively (in parallel).
        if (folder.TryGetLocalPath() is { } localSource && fs.GetNativePath(targetPath) is { } localTarget)
        {
            await Task.Run(() => FastCopy.CopyDirectory(localSource, localTarget, progress));
            return;
        }
        await fs.CreateDirectoryAsync(targetPath);
        await foreach (var item in folder.GetItemsAsync())
        {
            if (item is IStorageFile file)
            {
                progress?.Report(file.Name);
                await CopyFileAsync(fs, file, VPath.Combine(targetPath, file.Name));
            }
            else if (item is IStorageFolder sub)
            {
                await CopyFolderAsync(fs, sub, VPath.Combine(targetPath, sub.Name), progress);
            }
        }
    }

    /// <summary>Lists every file of a folder recursively, with its path relative to the folder.</summary>
    public static async Task<List<(IStorageFile File, string RelativeDirectory)>> ListFilesAsync(IStorageFolder folder, string relative = "")
    {
        var list = new List<(IStorageFile, string)>();
        await foreach (var item in folder.GetItemsAsync())
        {
            if (item is IStorageFile file)
                list.Add((file, relative));
            else if (item is IStorageFolder sub)
                list.AddRange(await ListFilesAsync(sub, VPath.Combine(relative, sub.Name)));
        }
        return list;
    }
}
