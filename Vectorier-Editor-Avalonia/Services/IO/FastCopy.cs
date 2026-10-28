using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace Vectorier_Editor_Avalonia.Services.IO;

/// <summary>
/// Forwards at most one report per interval (plus the last one). Copying a project reports thousands of
/// files; posting every one to the UI thread used to keep the window busy long after the copy finished.
/// </summary>
public sealed class ThrottledProgress<T> : IProgress<T>
{
    private readonly IProgress<T> _inner;
    private readonly long _intervalTicks;
    private long _last;

    public ThrottledProgress(IProgress<T> inner, TimeSpan interval)
    {
        _inner = inner;
        _intervalTicks = (long)(interval.TotalSeconds * Stopwatch.Frequency);
        _last = -_intervalTicks;
    }

    public void Report(T value)
    {
        long now = Stopwatch.GetTimestamp();
        long last = Interlocked.Read(ref _last);
        if (now - last < _intervalTicks)
            return;
        if (Interlocked.CompareExchange(ref _last, now, last) == last)
            _inner.Report(value);
    }
}

/// <summary>Directory copies used for creating, duplicating and publishing projects.</summary>
public static class FastCopy
{
    /// <summary>Native copy: all folders first, then the files in parallel (several files in flight at once).</summary>
    public static void CopyDirectory(string source, string target, IProgress<string>? progress, Func<string, bool>? exclude = null)
    {
        source = Path.GetFullPath(source);
        target = Path.GetFullPath(target);
        Directory.CreateDirectory(target);

        var dirs = new List<string>();
        var files = new List<string>();
        Collect(source, string.Empty, dirs, files, exclude);
        foreach (string dir in dirs)
            Directory.CreateDirectory(Path.Combine(target, dir));

        int done = 0;
        int total = files.Count;
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8) }, rel =>
        {
            File.Copy(Path.Combine(source, rel), Path.Combine(target, rel), true);
            int n = Interlocked.Increment(ref done);
            progress?.Report($"{n} / {total}  {Path.GetFileName(rel)}");
        });
    }

    private static void Collect(string root, string rel, List<string> dirs, List<string> files, Func<string, bool>? exclude)
    {
        string full = rel.Length == 0 ? root : Path.Combine(root, rel);
        foreach (string file in Directory.EnumerateFiles(full))
        {
            string r = rel.Length == 0 ? Path.GetFileName(file) : Path.Combine(rel, Path.GetFileName(file));
            if (exclude?.Invoke(r.Replace('\\', '/')) != true)
                files.Add(r);
        }
        foreach (string dir in Directory.EnumerateDirectories(full))
        {
            string r = rel.Length == 0 ? Path.GetFileName(dir) : Path.Combine(rel, Path.GetFileName(dir));
            if (exclude?.Invoke(r.Replace('\\', '/') + "/") == true)
                continue;
            dirs.Add(r);
            Collect(root, r, dirs, files, exclude);
        }
    }

    /// <summary>
    /// Storage-provider copy (browser, or a picked folder without a local path): the source folder is listed
    /// once, files are streamed (not read whole into memory twice), several at a time.
    /// </summary>
    public static async Task CopyFolderAsync(IStorageFolder source, IStorageFolder target, IProgress<string>? progress,
        Func<string, bool>? exclude = null, string rel = "")
    {
        var gate = new SemaphoreSlim(4);
        var tasks = new List<Task>();
        var subFolders = new List<IStorageFolder>();
        await foreach (var item in source.GetItemsAsync())
        {
            string r = rel.Length == 0 ? item.Name : rel + "/" + item.Name;
            if (item is IStorageFolder folder)
            {
                if (exclude?.Invoke(r + "/") != true)
                    subFolders.Add(folder);
                continue;
            }
            if (item is not IStorageFile file || exclude?.Invoke(r) == true)
                continue;
            await gate.WaitAsync();
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await CopyFileAsync(file, target);
                    progress?.Report(r);
                }
                finally
                {
                    gate.Release();
                }
            }));
        }
        await Task.WhenAll(tasks);

        foreach (var folder in subFolders)
        {
            var sub = await GetOrCreateFolderAsync(target, folder.Name);
            string r = rel.Length == 0 ? folder.Name : rel + "/" + folder.Name;
            await CopyFolderAsync(folder, sub, progress, exclude, r);
        }
    }

    /// <summary>Copies a directory of an <see cref="IFileSystem"/> into a picked folder.</summary>
    public static async Task CopyToStorageFolderAsync(IFileSystem fs, string sourceDir, IStorageFolder target, IProgress<string>? progress,
        Func<string, bool>? exclude = null, string rel = "")
    {
        foreach (string file in await fs.GetFilesAsync(sourceDir))
        {
            string name = VPath.GetFileName(file);
            string r = rel.Length == 0 ? name : rel + "/" + name;
            if (exclude?.Invoke(r) == true)
                continue;
            await using var input = await fs.OpenReadAsync(file);
            var created = await target.CreateFileAsync(name) ?? throw new IOException("Cannot create " + r);
            await using var output = await created.OpenWriteAsync();
            output.SetLength(0);
            await input.CopyToAsync(output);
            progress?.Report(r);
        }
        foreach (string dir in await fs.GetDirectoriesAsync(sourceDir))
        {
            string name = VPath.GetFileName(dir);
            string r = rel.Length == 0 ? name : rel + "/" + name;
            if (exclude?.Invoke(r + "/") == true)
                continue;
            var sub = await GetOrCreateFolderAsync(target, name);
            await CopyToStorageFolderAsync(fs, dir, sub, progress, exclude, r);
        }
    }

    public static async Task<IStorageFolder> GetOrCreateFolderAsync(IStorageFolder parent, string name)
    {
        IStorageFolder? folder = null;
        try { folder = await parent.GetFolderAsync(name); } catch { folder = null; }
        folder ??= await parent.CreateFolderAsync(name);
        return folder ?? throw new IOException("Cannot create folder " + name);
    }

    private static async Task CopyFileAsync(IStorageFile file, IStorageFolder target)
    {
        IStorageFile? existing = null;
        try { existing = await target.GetFileAsync(file.Name); } catch { existing = null; }
        var created = existing ?? await target.CreateFileAsync(file.Name) ?? throw new IOException("Cannot create " + file.Name);
        await using var input = await file.OpenReadAsync();
        await using var output = await created.OpenWriteAsync();
        output.SetLength(0);
        await input.CopyToAsync(output);
    }

    /// <summary>The editor's cache folder is never copied into a published project.</summary>
    public static bool IsCachePath(string relative) =>
        relative.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(p => p == ".vectorier");
}
