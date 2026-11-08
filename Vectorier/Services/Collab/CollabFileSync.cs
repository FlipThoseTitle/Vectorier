using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Vectorier.Services.Localization;

namespace Vectorier.Services.Collab;

/// <summary>
/// Syncs project files during a session (the shared level and the .vectorier cache excluded: the level is synced
/// live, the cache is per computer). The host's changes reach everyone. A participant's changes become a request
/// the host approves (installed and passed on to everyone) or declines (reverted to the host's version, as are
/// changes over 100 MB).
/// </summary>
public sealed class CollabFileSync : IDisposable
{
    private readonly CollabSession _session;
    private readonly string _root;
    private readonly string _levelPath;
    private readonly bool _isHost;
    private readonly FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _debounce;
    private readonly HashSet<string> _queued = new(StringComparer.Ordinal);
    private readonly HashSet<string> _queuedFolders = new(StringComparer.Ordinal);

    /// <summary>Files about to be written or removed by the sync itself (hash, or null for removed): not reported again.</summary>
    private readonly Dictionary<string, string?> _expected = new(StringComparer.Ordinal);

    /// <summary>Participant: the host's files as last known.</summary>
    private readonly Dictionary<string, ManifestEntry> _hostFiles;

    public CollabFileSync(CollabSession session, string root, string levelPath, bool isHost, Dictionary<string, ManifestEntry>? hostFiles = null)
    {
        _session = session;
        _root = root;
        _levelPath = levelPath;
        _isHost = isHost;
        _hostFiles = hostFiles ?? new Dictionary<string, ManifestEntry>(StringComparer.Ordinal);
        _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(1200), DispatcherPriority.Background, (_, _) => { _debounce!.Stop(); _ = ProcessAsync(); });
        try
        {
            _watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            _watcher.Changed += OnFsEvent;
            _watcher.Created += OnFsEvent;
            _watcher.Deleted += OnFsEvent;
            _watcher.Renamed += (s, e) =>
            {
                OnFsEvent(s, new FileSystemEventArgs(WatcherChangeTypes.Deleted, Path.GetDirectoryName(e.OldFullPath)!, e.OldName));
                OnFsEvent(s, e);
            };
            _watcher.Error += (_, _) => Dispatcher.UIThread.Post(() => { _queuedFolders.Add(string.Empty); _debounce.Stop(); _debounce.Start(); });
            _watcher.EnableRaisingEvents = true;
        }
        catch
        {
            _watcher = null; // no watching on this system: only the level is shared live
        }
    }

    public void Dispose()
    {
        _debounce.Stop();
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }
        foreach (var request in _incoming.Values)
            request.Stager.Dispose();
        _incoming.Clear();
        _download?.Stager.Dispose();
    }

    private Dictionary<string, ManifestEntry> Known => _isHost ? _session.Manifest : _hostFiles;

    // ================= WATCHING ================= //

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        string rel;
        try
        {
            rel = CollabProjectFiles.Relative(_root, e.FullPath);
        }
        catch
        {
            return;
        }
        if (rel.StartsWith("..", StringComparison.Ordinal) || CollabProjectFiles.IsIgnored(rel) || string.Equals(rel, _levelPath, StringComparison.OrdinalIgnoreCase))
            return;
        bool isFolder = Directory.Exists(e.FullPath) || e.ChangeType == WatcherChangeTypes.Deleted && !Path.HasExtension(e.FullPath);
        Dispatcher.UIThread.Post(() =>
        {
            if (isFolder)
                _queuedFolders.Add(rel);
            _queued.Add(rel);
            _debounce.Stop();
            _debounce.Start();
        });
    }

    /// <summary>What changed on disk since it was last looked at (files are hashed: only real content changes count).</summary>
    private async Task ProcessAsync()
    {
        var paths = _queued.ToList();
        var folders = _queuedFolders.ToList();
        _queued.Clear();
        _queuedFolders.Clear();
        var known = Known;
        string root = _root, level = _levelPath;

        // Folders created / removed / renamed: everything listed under them is looked at again.
        foreach (var folder in folders)
        {
            string prefix = folder.Length == 0 ? string.Empty : folder + "/";
            paths.AddRange(known.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)));
            string full = folder.Length == 0 ? root : CollabProjectFiles.Full(root, folder);
            if (Directory.Exists(full))
            {
                try
                {
                    paths.AddRange(Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories).Select(f => CollabProjectFiles.Relative(root, f)));
                }
                catch
                {
                }
            }
        }

        var candidates = paths.Distinct()
            .Where(p => !CollabProjectFiles.IsIgnored(p) && !string.Equals(p, level, StringComparison.OrdinalIgnoreCase) && CollabProjectFiles.IsSafe(root, p))
            .ToList();
        var states = await Task.Run(() => candidates.Select(p =>
        {
            string full = CollabProjectFiles.Full(root, p);
            return (Path: p, State: File.Exists(full) ? CollabProjectFiles.HashFile(full) : null, Exists: File.Exists(full));
        }).ToList());

        var changed = new List<ManifestEntry>();
        var removed = new List<string>();
        foreach (var (path, state, exists) in states)
        {
            if (exists && state == null)
                continue; // still being written: it comes back with the next event
            string? hash = state?.Hash;
            if (_expected.TryGetValue(path, out var expected) && expected == hash)
            {
                _expected.Remove(path);
                continue; // written by the sync itself
            }
            known.TryGetValue(path, out var current);
            if (state is { } s)
            {
                if (current == null || current.Hash != s.Hash)
                    changed.Add(new ManifestEntry(path, s.Size, s.Hash));
            }
            else if (current != null)
            {
                removed.Add(path);
            }
        }
        if (changed.Count == 0 && removed.Count == 0)
            return;

        if (_isHost)
        {
            foreach (var entry in changed)
                _session.Manifest[entry.Path] = entry;
            foreach (var path in removed)
                _session.Manifest.Remove(path);
            _session.BroadcastFilesChanged(changed, removed);
        }
        else
        {
            QueueRequest(changed, removed);
        }
    }

    // ================= PARTICIPANT: REQUESTS ================= //

    private sealed record Outgoing(string Id, List<ManifestEntry> Changed, List<string> Removed);

    private Outgoing? _outgoing;
    private readonly List<ManifestEntry> _laterChanged = new();
    private readonly List<string> _laterRemoved = new();

    private void QueueRequest(List<ManifestEntry> changed, List<string> removed)
    {
        if (_outgoing != null)
        {
            // One request at a time: the next one goes once this one is decided.
            _laterChanged.RemoveAll(e => changed.Any(c => c.Path == e.Path) || removed.Contains(e.Path));
            _laterRemoved.RemoveAll(p => changed.Any(c => c.Path == p));
            _laterChanged.AddRange(changed);
            _laterRemoved.AddRange(removed.Where(p => !_laterRemoved.Contains(p)));
            return;
        }
        long size = changed.Sum(e => e.Size);
        if (size > CollabProjectFiles.MaxRequestBytes)
        {
            Notice(Loc.F("Your changes to the project ({0}) are over the {1} limit for one request, so they were not sent to the host and were undone.",
                CollabProjectFiles.FormatSize(size), CollabProjectFiles.FormatSize(CollabProjectFiles.MaxRequestBytes)));
            Revert(changed.Select(e => e.Path).Concat(removed).ToList());
            return;
        }
        var request = new Outgoing(Guid.NewGuid().ToString("N"), changed, removed);
        _outgoing = request;
        _session.SendToHost(new ChangeRequestMsg(request.Id, changed, removed));
        Notice(Loc.F("Asked the host to approve your project changes ({0} file(s)).", changed.Count + removed.Count));
        if (changed.Count > 0)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _session.SendFilesToHostAsync(request.Id, changed, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Post(() => Notice(Loc.F("Sending your changes failed: {0}", ex.Message)));
                }
            });
        }
    }

    public void OnDecision(ChangeDecisionMsg decision)
    {
        if (_outgoing == null || _outgoing.Id != decision.Id)
            return;
        var request = _outgoing;
        _outgoing = null;
        if (decision.Approved)
        {
            foreach (var entry in request.Changed)
                _hostFiles[entry.Path] = entry;
            foreach (var path in request.Removed)
                _hostFiles.Remove(path);
            Notice(Loc.T("The host approved your project changes."));
        }
        else
        {
            Notice(decision.Reason ?? Loc.T("The host declined your project changes: they were undone."));
            Revert(request.Changed.Select(e => e.Path).Concat(request.Removed).ToList());
        }
        if (_laterChanged.Count > 0 || _laterRemoved.Count > 0)
        {
            var changed = _laterChanged.ToList();
            var removed = _laterRemoved.ToList();
            _laterChanged.Clear();
            _laterRemoved.Clear();
            QueueRequest(changed, removed);
        }
    }

    /// <summary>Puts these paths back as the host has them (downloads, or removes what the host does not have).</summary>
    private void Revert(List<string> paths)
    {
        var download = paths.Where(_hostFiles.ContainsKey).Select(p => _hostFiles[p]).ToList();
        var remove = paths.Where(p => !_hostFiles.ContainsKey(p) && File.Exists(CollabProjectFiles.Full(_root, p))).ToList();
        StartDownload(download, remove);
    }

    // ================= PARTICIPANT: HOST CHANGES ================= //

    private sealed record Download(CollabStager Stager, List<string> Removed);

    private Download? _download;
    private readonly Queue<(List<ManifestEntry> Changed, List<string> Removed)> _downloads = new();

    public void OnHostFilesChanged(FilesChangedMsg message)
    {
        var changed = new List<ManifestEntry>();
        foreach (var entry in message.Changed)
        {
            if (!CollabProjectFiles.IsSafe(_root, entry.Path))
                continue;
            _hostFiles[entry.Path] = entry;
            changed.Add(entry);
        }
        var removed = new List<string>();
        foreach (var path in message.Removed)
        {
            if (!CollabProjectFiles.IsSafe(_root, path))
                continue;
            _hostFiles.Remove(path);
            removed.Add(path);
        }
        // Only what differs here is fetched.
        var need = changed.Where(e => CollabProjectFiles.HashFile(CollabProjectFiles.Full(_root, e.Path)) is not { } mine || mine.Hash != e.Hash).ToList();
        var gone = removed.Where(p => File.Exists(CollabProjectFiles.Full(_root, p))).ToList();
        StartDownload(need, gone);
    }

    private void StartDownload(List<ManifestEntry> files, List<string> removed)
    {
        if (files.Count == 0 && removed.Count == 0)
            return;
        if (_download != null)
        {
            _downloads.Enqueue((files, removed));
            return;
        }
        var stager = new CollabStager(_root, "update", files);
        _download = new Download(stager, removed);
        var missing = stager.Missing.Select(e => e.Path).ToList();
        if (missing.Count > 0)
            _session.SendToHost(new FileRequestMsg(missing));
        else
            _ = FinishDownloadAsync();
    }

    public void OnChunk(FileChunkMsg chunk, byte[] bytes)
    {
        if (_download == null || chunk.Transfer != "update")
            return;
        try
        {
            _download.Stager.Write(chunk, bytes);
        }
        catch (Exception ex)
        {
            Notice(Loc.F("A project file could not be updated: {0}", ex.Message));
            _download.Stager.Clean();
            _download = null;
            NextDownload();
            return;
        }
        if (_download.Stager.IsComplete)
            _ = FinishDownloadAsync();
    }

    private async Task FinishDownloadAsync()
    {
        var download = _download!;
        var files = download.Stager.Completed.ToDictionary(k => k.Key, v => v.Value);
        foreach (var path in files.Keys)
            _expected[path] = _hostFiles.TryGetValue(path, out var e) ? e.Hash : null;
        foreach (var path in download.Removed)
            _expected[path] = null;
        try
        {
            CollabProjectFiles.Install(_root, files, download.Removed);
            download.Stager.Clean();
            if (_session.Editor is { } editor)
                await editor.RefreshAfterFileSyncAsync(files.Keys.Concat(download.Removed).ToList());
        }
        catch (Exception ex)
        {
            foreach (var path in files.Keys.Concat(download.Removed))
                _expected.Remove(path);
            Notice(Loc.F("Project files could not be updated: {0}", ex.Message));
        }
        _download = null;
        NextDownload();
    }

    private void NextDownload()
    {
        if (_downloads.Count > 0)
        {
            var (files, removed) = _downloads.Dequeue();
            StartDownload(files, removed);
        }
    }

    // ================= HOST: REQUESTS FROM PARTICIPANTS ================= //

    private sealed record Incoming(int Peer, string Name, ChangeRequestMsg Request, CollabStager Stager);

    private readonly Dictionary<string, Incoming> _incoming = new(StringComparer.Ordinal);
    private bool _deciding;
    private readonly Queue<Incoming> _decisions = new();

    public void OnRequest(int peer, string name, ChangeRequestMsg request)
    {
        // Checked here, whatever the participant's own Vectorier did: paths, the shared level, the size limit.
        string? problem = null;
        if (request.Changed.Count + request.Removed.Count is 0 or > 5000 || _incoming.ContainsKey(request.Id) || request.Id.Length > 64)
            problem = Loc.T("Invalid request.");
        else if (request.Changed.Any(e => !CollabProjectFiles.IsSafe(_root, e.Path) || e.Size < 0 || string.Equals(e.Path, _levelPath, StringComparison.OrdinalIgnoreCase))
                 || request.Removed.Any(p => !CollabProjectFiles.IsSafe(_root, p) || string.Equals(p, _levelPath, StringComparison.OrdinalIgnoreCase)))
            problem = Loc.T("The request has paths outside the project.");
        else if (request.Changed.Sum(e => e.Size) > CollabProjectFiles.MaxRequestBytes)
            problem = Loc.F("The request is over the {0} limit.", CollabProjectFiles.FormatSize(CollabProjectFiles.MaxRequestBytes));
        if (problem != null)
        {
            _session.SendTo(peer, new ChangeDecisionMsg(request.Id, false, problem));
            return;
        }
        var incoming = new Incoming(peer, name, request, new CollabStager(_root, "request-" + request.Id, request.Changed));
        _incoming[request.Id] = incoming;
        if (incoming.Stager.IsComplete)
            QueueDecision(incoming);
    }

    public void OnRequestChunk(int peer, FileChunkMsg chunk, byte[] bytes)
    {
        // Only into a request this participant made, with the sizes and contents it announced.
        if (!_incoming.TryGetValue(chunk.Transfer, out var incoming) || incoming.Peer != peer)
            return;
        try
        {
            incoming.Stager.Write(chunk, bytes);
        }
        catch (Exception ex)
        {
            _incoming.Remove(chunk.Transfer);
            incoming.Stager.Clean();
            _session.SendTo(peer, new ChangeDecisionMsg(chunk.Transfer, false, Loc.F("The upload failed: {0}", ex.Message)));
            return;
        }
        if (incoming.Stager.IsComplete)
            QueueDecision(incoming);
    }

    private void QueueDecision(Incoming incoming)
    {
        _decisions.Enqueue(incoming);
        _ = DecideAsync();
    }

    private async Task DecideAsync()
    {
        if (_deciding)
            return;
        _deciding = true;
        try
        {
            while (_decisions.Count > 0)
            {
                var incoming = _decisions.Dequeue();
                if (!_incoming.ContainsKey(incoming.Request.Id))
                    continue;
                var request = incoming.Request;
                var lines = new List<string>();
                foreach (var entry in request.Changed.Take(12))
                    lines.Add((Known.ContainsKey(entry.Path) ? Loc.T("Replace") : Loc.T("Add")) + ": " + entry.Path + " (" + CollabProjectFiles.FormatSize(entry.Size) + ")");
                foreach (var path in request.Removed.Take(12))
                    lines.Add(Loc.T("Remove") + ": " + path);
                int more = request.Changed.Count + request.Removed.Count - lines.Count;
                if (more > 0)
                    lines.Add(Loc.F("…and {0} more", more));
                string text = Loc.F("{0} wants to change the shared project ({1} in total):", incoming.Name, CollabProjectFiles.FormatSize(request.Changed.Sum(e => e.Size)))
                    + "\n\n" + string.Join("\n", lines);
                bool approve = await AppServices.Dialogs.ConfirmAsync(Loc.T("Project Change Request"), text, Loc.T("Approve"), Loc.T("Decline"));
                _incoming.Remove(request.Id);
                if (!approve)
                {
                    incoming.Stager.Clean();
                    _session.SendTo(incoming.Peer, new ChangeDecisionMsg(request.Id, false, null));
                    continue;
                }
                var files = incoming.Stager.Completed.ToDictionary(k => k.Key, v => v.Value);
                foreach (var entry in request.Changed)
                    _expected[entry.Path] = entry.Hash;
                foreach (var path in request.Removed)
                    _expected[path] = null;
                try
                {
                    CollabProjectFiles.Install(_root, files, request.Removed.Where(p => File.Exists(CollabProjectFiles.Full(_root, p))).ToList());
                    incoming.Stager.Clean();
                }
                catch (Exception ex)
                {
                    foreach (var path in request.Changed.Select(e => e.Path).Concat(request.Removed))
                        _expected.Remove(path);
                    _session.SendTo(incoming.Peer, new ChangeDecisionMsg(request.Id, false, Loc.F("The host could not apply the changes: {0}", ex.Message)));
                    continue;
                }
                foreach (var entry in request.Changed)
                    _session.Manifest[entry.Path] = entry;
                foreach (var path in request.Removed)
                    _session.Manifest.Remove(path);
                _session.SendTo(incoming.Peer, new ChangeDecisionMsg(request.Id, true, null));
                _session.BroadcastFilesChanged(request.Changed, request.Removed);
                if (_session.Editor is { } editor)
                    await editor.RefreshAfterFileSyncAsync(request.Changed.Select(e => e.Path).Concat(request.Removed).ToList());
            }
        }
        finally
        {
            _deciding = false;
        }
    }

    /// <summary>A participant left: its unfinished requests are dropped.</summary>
    public void PeerGone(int peer)
    {
        foreach (var incoming in _incoming.Values.Where(i => i.Peer == peer).ToList())
        {
            _incoming.Remove(incoming.Request.Id);
            incoming.Stager.Clean();
        }
    }

    private void Notice(string text) => _session.RaiseNotice(text);
}
