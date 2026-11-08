using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Vectorier.Services.Collab;

/// <summary>
/// The shared project's files on disk: listing them with their SHA-256 (the "manifest"), checking paths that come from
/// the network, receiving files into a staging folder, and installing them so that a failure never leaves the project
/// half-changed (originals are backed up first, and an interrupted install is rolled back the next time).
/// The project's .vectorier folder (editor cache, backups) is never listed, sent or replaced.
/// </summary>
public static class CollabProjectFiles
{
    public const string CacheFolder = ".vectorier";
    public const long MaxRequestBytes = 100L * 1024 * 1024;
    public const int ChunkSize = 256 * 1024;

    private static readonly HashSet<string> Junk = new(StringComparer.OrdinalIgnoreCase) { "Thumbs.db", ".DS_Store", "desktop.ini" };

    public static string CollabFolder(string root) => Path.Combine(root, CacheFolder, "collab");

    // ================= PATHS ================= //

    /// <summary>
    /// True for a relative project path that is safe to read or write: '/' separated, no "..", not absolute, not in the
    /// cache folder, and inside the project once resolved.
    /// </summary>
    public static bool IsSafe(string root, string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Length > 400 || relative.Contains('\\') || relative.Contains(':') || relative.Contains('\0'))
            return false;
        if (relative.StartsWith('/') || Path.IsPathRooted(relative))
            return false;
        var parts = relative.Split('/');
        if (parts.Any(p => p.Length == 0 || p == "." || p == ".." || p.Trim() != p && p.Trim().Length == 0))
            return false;
        if (string.Equals(parts[0], CacheFolder, StringComparison.OrdinalIgnoreCase) || Junk.Contains(parts[^1]))
            return false;
        string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        string rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(rootFull, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    public static string Full(string root, string relative) => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    public static string Relative(string root, string full) =>
        Path.GetRelativePath(root, full).Replace(Path.DirectorySeparatorChar, '/');

    public static bool IsIgnored(string relative)
    {
        var first = relative.Split('/')[0];
        return string.Equals(first, CacheFolder, StringComparison.OrdinalIgnoreCase) || Junk.Contains(relative[(relative.LastIndexOf('/') + 1)..])
            || relative.EndsWith(".part", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
    }

    // ================= MANIFEST ================= //

    public static async Task<Dictionary<string, ManifestEntry>> BuildManifestAsync(string root, CancellationToken token = default)
    {
        return await Task.Run(() =>
        {
            var result = new Dictionary<string, ManifestEntry>(StringComparer.Ordinal);
            if (!Directory.Exists(root))
                return result;
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                token.ThrowIfCancellationRequested();
                string rel = Relative(root, file);
                if (IsIgnored(rel))
                    continue;
                if (HashFile(file) is { } entry)
                    result[rel] = new ManifestEntry(rel, entry.Size, entry.Hash);
            }
            return result;
        }, token);
    }

    /// <summary>Size and SHA-256 of a file; null when it cannot be read (being written, deleted meanwhile).</summary>
    public static (long Size, string Hash)? HashFile(string full)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var hash = SHA256.HashData(stream);
                return (stream.Length, Convert.ToHexString(hash));
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
            catch (IOException)
            {
                Thread.Sleep(150);
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
        return null;
    }

    public static string FormatSize(long bytes) => bytes < 1024 * 1024
        ? $"{Math.Max(1, (bytes + 1023) / 1024)} KB"
        : $"{bytes / (1024.0 * 1024.0):0.0} MB";

    // ================= SENDING ================= //

    /// <summary>Sends files as chunks (lower priority than level edits).</summary>
    public static async Task SendFilesAsync(CollabConnection connection, string root, string transfer, IEnumerable<ManifestEntry> files, CancellationToken token)
    {
        foreach (var file in files)
        {
            string full = Full(root, file.Path);
            using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long size = stream.Length;
            if (size == 0)
            {
                await connection.SendBulkAsync(new FileChunkMsg(transfer, file.Path, 0, 0, file.Hash), Array.Empty<byte>(), token);
                continue;
            }
            var buffer = new byte[ChunkSize];
            long offset = 0;
            while (offset < size)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(ChunkSize, size - offset)), token);
                if (read <= 0)
                    break;
                await connection.SendBulkAsync(new FileChunkMsg(transfer, file.Path, offset, size, file.Hash), buffer.AsSpan(0, read).ToArray(), token);
                offset += read;
            }
        }
    }

    // ================= JOURNALLED INSTALL ================= //

    private sealed record JournalEntry(string Path, bool HadOriginal, bool Installs);

    private sealed record Journal(string Backup, List<JournalEntry> Entries);

    private static readonly JsonSerializerOptions JournalJson = new() { WriteIndented = true };

    /// <summary>
    /// Puts staged files in place and removes others, as one unit: everything replaced or removed is first moved to a
    /// dated backup folder, and a journal lists the plan, so an interrupted install can be undone (<see cref="Recover"/>).
    /// </summary>
    public static void Install(string root, IReadOnlyDictionary<string, string> staged, IReadOnlyCollection<string> removed)
    {
        Recover(root);
        string collab = CollabFolder(root);
        Directory.CreateDirectory(collab);
        string backup = Path.Combine(collab, "backup", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        var entries = new List<JournalEntry>();
        foreach (var path in staged.Keys.Concat(removed).Distinct())
        {
            if (!IsSafe(root, path))
                throw new InvalidDataException("Unsafe path: " + path);
            entries.Add(new JournalEntry(path, File.Exists(Full(root, path)), staged.ContainsKey(path)));
        }
        string journalPath = Path.Combine(collab, "journal.json");
        File.WriteAllText(journalPath, JsonSerializer.Serialize(new Journal(backup, entries), JournalJson));

        try
        {
            foreach (var entry in entries.Where(e => e.HadOriginal))
            {
                string target = Full(backup, entry.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(Full(root, entry.Path), target, true);
            }
            foreach (var (path, source) in staged)
            {
                string target = Full(root, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, true);
            }
        }
        catch
        {
            // Back to how it was before this install, then report the failure.
            Recover(root);
            throw;
        }
        File.Delete(journalPath);
        foreach (var source in staged.Values)
        {
            try { File.Delete(source); } catch { }
        }
        PruneBackups(Path.Combine(collab, "backup"), keep: 5);
    }

    /// <summary>Undoes an install that was interrupted (the journal is still there).</summary>
    public static void Recover(string root)
    {
        string journalPath = Path.Combine(CollabFolder(root), "journal.json");
        if (!File.Exists(journalPath))
            return;
        Journal? journal = null;
        try
        {
            journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(journalPath), JournalJson);
        }
        catch
        {
        }
        if (journal != null)
        {
            foreach (var entry in journal.Entries)
            {
                if (!IsSafe(root, entry.Path))
                    continue;
                string current = Full(root, entry.Path);
                string saved = Full(journal.Backup, entry.Path);
                try
                {
                    if (entry.HadOriginal)
                    {
                        // Moved to the backup already: put it back (over whatever was installed).
                        if (File.Exists(saved))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(current)!);
                            File.Move(saved, current, true);
                        }
                    }
                    else if (entry.Installs && File.Exists(current))
                    {
                        File.Delete(current); // was new: the half-done install is taken back out
                    }
                }
                catch
                {
                    // Keep going: as much as possible is restored.
                }
            }
        }
        File.Delete(journalPath);
    }

    private static void PruneBackups(string folder, int keep)
    {
        try
        {
            foreach (var old in Directory.GetDirectories(folder).OrderByDescending(d => d, StringComparer.Ordinal).Skip(keep))
                Directory.Delete(old, true);
        }
        catch
        {
        }
    }
}
