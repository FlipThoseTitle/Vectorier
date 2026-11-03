using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalonia.Threading;
using Vectorier.Services.IO;
using Vectorier.Services.Localization;
using Vectorier.ViewModels.Editor;

namespace Vectorier.Services.Collab;

/// <summary>What the join process shows and asks while it runs.</summary>
public interface ICollabJoinUi
{
    void Report(string text, double? fraction);

    Task<bool> AskAsync(string title, string message, string yes, string no);

    /// <summary>The shared level is about to open in a new editor window (the progress can close).</summary>
    void Finish();
}

public sealed partial class CollabSession
{
    /// <summary>
    /// Joins a host. In order: the host must accept (nothing is downloaded before that); the local copy of the project
    /// is found or created (never an unrelated project with the same name); it is compared with the host's files and
    /// only what differs is downloaded, verified and installed; then the host's level opens. Returns null when joined,
    /// otherwise why not.
    /// </summary>
    public async Task<string?> JoinAsync(CollabCode code, string userName, ICollabJoinUi ui, CancellationToken token)
    {
        if (IsActive)
            return Loc.T("You are already in a Live Collaboration session.");
        var inbox = Channel.CreateUnbounded<(CollabMsg Message, byte[] Bytes)>();
        CollabConnection connection;
        ui.Report(Loc.F("Connecting to {0}…", $"{code.Host}:{code.Port}"), null);
        try
        {
            connection = await CollabConnection.ConnectAsync(code, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return Loc.T("Cancelled.");
        }
        catch (Exception ex)
        {
            return Loc.F("Could not reach the host at {0}.\n\nMake sure Radmin VPN is running on both computers and that you are in the same network as the host, and that the Session ID is the current one.\n\n{1}", $"{code.Host}:{code.Port}", ex.Message);
        }

        void OnReceived(CollabConnection c, CollabMsg m, byte[] b) => inbox.Writer.TryWrite((m, b));
        void OnClosed(CollabConnection c, string? r) => inbox.Writer.TryComplete();
        connection.Received += OnReceived;
        connection.Closed += OnClosed;
        connection.Start();
        connection.Send(new HelloMsg(CollabWire.Protocol, Convert.ToBase64String(code.Secret), SafeName(userName), null));

        async Task<(CollabMsg Message, byte[] Bytes)> NextAsync()
        {
            while (true)
            {
                if (!await inbox.Reader.WaitToReadAsync(token))
                    throw new CollabJoinException(Loc.T("The connection to the host was lost."));
                if (inbox.Reader.TryRead(out var item))
                {
                    if (item.Message is ClosedMsg closed)
                        throw new CollabJoinException(closed.Reason switch
                        {
                            "declined" => Loc.T("The host declined your request to join."),
                            "auth" => Loc.T("The Session ID is not valid anymore (the host may have restarted the session)."),
                            "version" => Loc.T("The host uses another version of Vectorier."),
                            "busy" => Loc.T("The session is full."),
                            "stopped" => Loc.T("The host ended the session."),
                            _ => Loc.T("The host closed the connection."),
                        });
                    return item;
                }
            }
        }

        bool joined = false;
        try
        {
            // 1. The host's approval.
            AcceptedMsg? accepted = null;
            while (accepted == null)
            {
                var (message, _) = await NextAsync();
                if (message is PendingMsg)
                    ui.Report(Loc.T("Waiting for the host to accept…"), null);
                else if (message is AcceptedMsg a)
                    accepted = a;
            }
            if (!CollabProjectFiles.IsSafe("/", accepted.LevelPath) || !accepted.LevelPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                throw new CollabJoinException(Loc.T("The host sent an invalid level path."));

            // 2. This computer's copy of the project.
            ui.Report(Loc.T("Preparing the project…"), null);
            var resolution = await CollabProjectLink.ResolveAsync(accepted.ProjectName, accepted.ProjectKey);
            if (resolution.Problem != null)
                throw new CollabJoinException(resolution.Problem);
            string root = resolution.Root!;
            CollabProjectFiles.Recover(root);

            // 3. Comparing the files (content hashes, not names or sizes).
            ManifestMsg? manifest = null;
            while (manifest == null)
            {
                var (message, _) = await NextAsync();
                if (message is ManifestMsg m)
                    manifest = m;
            }
            var hostFiles = new Dictionary<string, ManifestEntry>(StringComparer.Ordinal);
            foreach (var entry in manifest.Files)
            {
                if (!CollabProjectFiles.IsSafe(root, entry.Path) || entry.Size < 0)
                    throw new CollabJoinException(Loc.F("The host listed an unsafe file path: {0}", entry.Path));
                hostFiles[entry.Path] = entry;
            }
            ui.Report(Loc.T("Checking the project files…"), null);
            var local = await CollabProjectFiles.BuildManifestAsync(root, token);
            var download = hostFiles.Values.Where(e => !local.TryGetValue(e.Path, out var mine) || mine.Hash != e.Hash).ToList();
            var extra = local.Keys.Where(p => !hostFiles.ContainsKey(p)).ToList();
            if (download.Count > 0 || extra.Count > 0)
            {
                long bytes = download.Sum(e => e.Size);
                string message = Loc.F("Your copy of \"{0}\" differs from the host's.", accepted.ProjectName) + "\n\n";
                if (download.Count > 0)
                    message += Loc.F("{0} file(s) to download ({1}).", download.Count, CollabProjectFiles.FormatSize(bytes)) + "\n";
                if (extra.Count > 0)
                    message += Loc.F("{0} file(s) the host does not have will be moved to a backup (.vectorier/collab/backup).", extra.Count) + "\n";
                if (!await ui.AskAsync(Loc.T("Project Files"), message.TrimEnd(), Loc.T("Download and Join"), Loc.T("Cancel")))
                    throw new CollabJoinException(Loc.T("Cancelled."));
            }

            // 4. Downloading into the staging folder, verified file by file.
            var stager = new CollabStager(root, "join", download);
            try
            {
                var missing = stager.Missing.ToList();
                if (missing.Count > 0)
                {
                    long total = missing.Sum(e => e.Size);
                    connection.Send(new FileRequestMsg(missing.Select(e => e.Path).ToList()));
                    while (!stager.IsComplete)
                    {
                        var (message, bytes) = await NextAsync();
                        if (message is FileChunkMsg chunk)
                        {
                            stager.Write(chunk, bytes);
                            ui.Report(Loc.F("Downloading project files… {0} of {1}", CollabProjectFiles.FormatSize(stager.ReceivedBytes), CollabProjectFiles.FormatSize(total)),
                                total == 0 ? null : Math.Min(1, stager.ReceivedBytes / (double)total));
                        }
                    }
                }
                // 5. Installing: backups first, all or nothing.
                ui.Report(Loc.T("Installing the project files…"), null);
                CollabProjectFiles.Install(root, stager.Completed, extra);
                stager.Clean();
            }
            finally
            {
                stager.Dispose();
            }

            // 6. The level.
            connection.Send(new SyncedMsg());
            ui.Report(Loc.T("Loading the shared level…"), null);
            List<LevelOp>? snapshot = null;
            while (snapshot == null)
            {
                var (message, bytes) = await NextAsync();
                if (message is LevelMsg)
                    snapshot = Decompress(bytes);
            }

            var project = resolution.Project!;
            ui.Finish();
            var editor = new EditorViewModel(project, accepted.LevelName, VPath.Combine(project.Path, accepted.LevelPath));
            AppServices.Shell.Show(editor);
            var started = DateTime.UtcNow;
            while (editor.IsLoading && DateTime.UtcNow - started < TimeSpan.FromSeconds(60))
                await Task.Delay(50, token);

            // 7. Live: from here on, messages go to the session.
            MyId = accepted.YourId;
            _pending.Clear();
            _nextBatch = 0;
            _connection = connection;
            Role = CollabRole.Participant;
            Code = code.ToString();
            AttachEditor(editor, root, accepted.LevelPath);
            _sync!.LoadSnapshot(snapshot);
            _files = new CollabFileSync(this, root, accepted.LevelPath, isHost: false, hostFiles);
            connection.Received -= OnReceived;
            connection.Closed -= OnClosed;
            connection.Received += (c, m, b) => Dispatcher.UIThread.Post(() => ParticipantReceive(m, b));
            connection.Closed += (c, r) => Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(_connection, c))
                    EndFromHost(r ?? "lost");
            });
            // Whatever arrived meanwhile.
            while (inbox.Reader.TryRead(out var late))
                ParticipantReceive(late.Message, late.Bytes);
            if (!connection.IsOpen)
                EndFromHost("lost");
            joined = true;
            return null;
        }
        catch (CollabJoinException ex)
        {
            return ex.Message;
        }
        catch (OperationCanceledException)
        {
            return Loc.T("Cancelled.");
        }
        catch (Exception ex)
        {
            return Loc.F("Joining failed: {0}\n\nYour project was not changed (an interrupted install is undone automatically).", ex.Message);
        }
        finally
        {
            if (!joined)
                await connection.CloseWithAsync(new LeaveMsg());
        }
    }

    private sealed class CollabJoinException(string message) : Exception(message);
}
