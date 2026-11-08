using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Vectorier.Services.Collab;

/// <summary>
/// Receives file chunks into the staging folder (.vectorier/collab/staging). A file counts once all its bytes arrived
/// and its SHA-256 matches; a file already staged with the right content (an earlier, interrupted download) is reused.
/// </summary>
public sealed class CollabStager : IDisposable
{
    private readonly string _root;
    private readonly string _staging;
    private readonly Dictionary<string, ManifestEntry> _expected;
    private readonly Dictionary<string, FileStream> _open = new(StringComparer.Ordinal);

    public CollabStager(string root, string name, IEnumerable<ManifestEntry> expected)
    {
        _root = root;
        _staging = Path.Combine(CollabProjectFiles.CollabFolder(root), "staging", name);
        Directory.CreateDirectory(_staging);
        _expected = expected.ToDictionary(e => e.Path, StringComparer.Ordinal);
        foreach (var entry in _expected.Values)
        {
            string done = StagedPath(entry.Path);
            if (File.Exists(done) && CollabProjectFiles.HashFile(done) is { } existing && existing.Hash == entry.Hash)
                Completed[entry.Path] = done;
        }
    }

    /// <summary>Files fully received and verified: project path → staged file.</summary>
    public Dictionary<string, string> Completed { get; } = new(StringComparer.Ordinal);

    public IEnumerable<ManifestEntry> Missing => _expected.Values.Where(e => !Completed.ContainsKey(e.Path));

    public long ReceivedBytes { get; private set; }

    public bool IsComplete => Completed.Count == _expected.Count;

    private string StagedPath(string relative) => CollabProjectFiles.Full(_staging, relative);

    /// <summary>Writes a chunk. Throws when it does not fit what was expected (wrong file, size, or content).</summary>
    public void Write(FileChunkMsg chunk, byte[] bytes)
    {
        if (!_expected.TryGetValue(chunk.Path, out var entry) || !CollabProjectFiles.IsSafe(_root, chunk.Path))
            throw new InvalidDataException("Unexpected file: " + chunk.Path);
        if (Completed.ContainsKey(chunk.Path))
            return;
        if (chunk.Size != entry.Size || chunk.Hash != entry.Hash || chunk.Offset < 0 || chunk.Offset + bytes.Length > entry.Size)
            throw new InvalidDataException("Unexpected data for " + chunk.Path);
        string part = StagedPath(chunk.Path) + ".part";
        if (!_open.TryGetValue(chunk.Path, out var stream))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(part)!);
            stream = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None);
            _open[chunk.Path] = stream;
        }
        if (stream.Position != chunk.Offset)
            throw new InvalidDataException("Chunks out of order for " + chunk.Path);
        stream.Write(bytes);
        ReceivedBytes += bytes.Length;
        if (stream.Position == entry.Size)
        {
            stream.Dispose();
            _open.Remove(chunk.Path);
            if (CollabProjectFiles.HashFile(part) is not { } got || got.Hash != entry.Hash)
            {
                File.Delete(part);
                throw new InvalidDataException("The download of " + chunk.Path + " is damaged.");
            }
            string done = StagedPath(chunk.Path);
            File.Move(part, done, true);
            Completed[chunk.Path] = done;
        }
    }

    public void Dispose()
    {
        foreach (var stream in _open.Values)
            stream.Dispose();
        _open.Clear();
    }

    /// <summary>Removes the staging folder once its files are installed.</summary>
    public void Clean()
    {
        Dispose();
        try
        {
            Directory.Delete(_staging, true);
        }
        catch
        {
        }
    }
}
