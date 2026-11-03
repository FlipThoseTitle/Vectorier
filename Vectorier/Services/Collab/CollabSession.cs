using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Vectorier.Services.Localization;
using Vectorier.Services.Projects;
using Vectorier.ViewModels.Editor;

namespace Vectorier.Services.Collab;

public enum CollabRole
{
    None,
    Host,
    Participant,
}

/// <summary>
/// Live Collaboration for this editor: hosting (others connect directly to this computer) or taking part in someone
/// else's session. The host is the authority: it approves who joins, puts everyone's level edits in one order, serves
/// the project's files, and decides on requests to change them. Everything here runs on the UI thread; network
/// messages are handed over to it.
/// </summary>
public sealed partial class CollabSession : ObservableObject
{
    public static CollabSession Current { get; } = new();

    private CollabSession()
    {
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive), nameof(IsHost), nameof(IsParticipant), nameof(IsIdle))]
    private CollabRole _role;

    [ObservableProperty] private string? _code;

    public bool IsActive => Role != CollabRole.None;
    public bool IsHost => Role == CollabRole.Host;
    public bool IsParticipant => Role == CollabRole.Participant;
    public bool IsIdle => Role == CollabRole.None;

    /// <summary>Everyone in the session, host first.</summary>
    public ObservableCollection<CollabUser> Users { get; } = new();

    public int MyId { get; private set; }

    /// <summary>Where the others are and what they hold, by user id.</summary>
    public Dictionary<int, PresenceMsg> Presence { get; } = new();

    public event Action? PresenceChanged;

    /// <summary>Participant: the session ended without asking (closed by the host, kicked, connection lost). Text for the prompt.</summary>
    public event Action<string, EditorViewModel?>? Ended;

    /// <summary>A line to show in the editor's status bar.</summary>
    public event Action<string>? Notice;

    internal void RaiseNotice(string text) => Notice?.Invoke(text);

    private EditorViewModel? _editor;
    private CollabLevelSync? _sync;
    private CollabFileSync? _files;
    private string _projectRoot = string.Empty;
    private string _levelPath = string.Empty;
    private DispatcherTimer? _presenceTimer;
    private PresenceMsg? _lastPresence;

    public EditorViewModel? Editor => _editor;

    public CollabLevelSync? LevelSync => _sync;

    /// <summary>The project's folder on this computer (native path).</summary>
    public string ProjectRoot => _projectRoot;

    /// <summary>The shared level's file, relative to the project folder.</summary>
    public string LevelPath => _levelPath;

    internal static string SafeName(string name)
    {
        var cleaned = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return cleaned.Length == 0 ? "?" : cleaned.Length > 24 ? cleaned[..24] : cleaned;
    }

    private static uint RandomColor(int seed)
    {
        // Evenly spread hues (golden angle), bright and saturated so they stand out on the level.
        double hue = (seed * 137.508 + Random.Shared.Next(0, 360)) % 360;
        double s = 0.75, v = 0.95, c = v * s, x = c * (1 - Math.Abs(hue / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = hue switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return 0xFF000000u | (uint)((r + m) * 255) << 16 | (uint)((g + m) * 255) << 8 | (uint)((b + m) * 255);
    }

    public CollabUser? UserById(int id) => Users.FirstOrDefault(u => u.Id == id);

    public string DisplayName(CollabUser user) => user.IsHost ? Loc.F("{0} (Host)", user.Name) : user.Name;

    // ================= SHARED: LEVEL SYNC AND PRESENCE ================= //

    private void AttachEditor(EditorViewModel editor, string projectRoot, string levelPath)
    {
        _editor = editor;
        _projectRoot = projectRoot;
        _levelPath = levelPath;
        _sync = new CollabLevelSync(editor, MyId, OnLocalOps);
        editor.IsSelectionBlocked = IsBlockedByOthers;
        _presenceTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, (_, _) => SendPresence());
        _presenceTimer.Start();
    }

    private void DetachEditor()
    {
        _presenceTimer?.Stop();
        _presenceTimer = null;
        _files?.Dispose();
        _files = null;
        _sync?.Dispose();
        _sync = null;
        if (_editor != null)
        {
            _editor.IsSelectionBlocked = null;
            _editor.SceneChangedByCollab();
        }
        _editor = null;
        _lastPresence = null;
        Presence.Clear();
        PresenceChanged?.Invoke();
    }

    /// <summary>Elements in a top-level hierarchy someone else's Dynamic Editor is working on cannot be selected here.</summary>
    private bool IsBlockedByOthers(Models.Scene.SceneElement element)
    {
        long root = element.Root.SyncId;
        if (root == 0)
            return false;
        foreach (var (user, presence) in Presence)
        {
            if (user != MyId && presence.DynamicRoots.Contains(root))
                return true;
        }
        return false;
    }

    private void SendPresence()
    {
        if (_editor == null || !IsActive)
            return;
        var center = _editor.ScenePointer ?? _editor.SceneViewCenter;
        var presence = new PresenceMsg(MyId, Math.Round(center.X, 1), Math.Round(center.Y, 1),
            _editor.Selection.Select(e => e.SyncId).Where(id => id != 0).ToList(), _editor.DynamicEditorRoots());
        if (_lastPresence is { } last && last.X == presence.X && last.Y == presence.Y
            && last.Selection.SequenceEqual(presence.Selection) && last.DynamicRoots.SequenceEqual(presence.DynamicRoots))
            return;
        _lastPresence = presence;
        if (IsHost)
            BroadcastActive(presence, except: -1);
        else
            _connection?.Send(presence);
    }

    private void OnPresence(PresenceMsg presence)
    {
        Presence[presence.User] = presence;
        // Someone just started editing a hierarchy in their Dynamic Editor: it is let go here.
        if (presence.DynamicRoots.Count > 0)
            _editor?.DropBlockedSelection();
        PresenceChanged?.Invoke();
    }

    private void OnLocalOps(List<LevelOp> ops)
    {
        if (IsHost)
            HostPublish(ops, author: 0, batch: 0);
        else if (IsParticipant)
            ParticipantSend(ops);
    }

    private static byte[] Compress(List<LevelOp> ops)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
            JsonSerializer.Serialize(gzip, ops, CollabJsonContext.Default.ListLevelOp);
        return output.ToArray();
    }

    private static List<LevelOp> Decompress(byte[] bytes)
    {
        using var input = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
        using var limited = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            limited.Write(buffer, 0, read);
            if (limited.Length > 512L * 1024 * 1024)
                throw new InvalidDataException("Level data too large.");
        }
        limited.Position = 0;
        return JsonSerializer.Deserialize(limited, CollabJsonContext.Default.ListLevelOp) ?? new List<LevelOp>();
    }

    // ================= HOST ================= //

    private sealed class Peer
    {
        public required int Id;
        public required CollabConnection Connection;
        public string Name = "?";
        public uint Color;
        public PeerState State = PeerState.Hello;
        public CancellationTokenSource Transfers = new();
    }

    private enum PeerState
    {
        Hello,
        Waiting,
        Syncing,
        Active,
    }

    private TcpListener? _listener;
    private CancellationTokenSource? _hostCts;
    private X509Certificate2? _certificate;
    private byte[] _secret = Array.Empty<byte>();
    private readonly Dictionary<int, Peer> _peers = new();
    private readonly Dictionary<string, (int Count, DateTime Since)> _failures = new();
    private int _nextPeerId = 1;
    private long _sequence;
    private string _projectKey = string.Empty;
    private ProjectInfo? _project;
    private Dictionary<string, ManifestEntry> _manifest = new(StringComparer.Ordinal);
    private readonly Queue<Peer> _approvals = new();
    private bool _approving;

    /// <summary>The host's current list of project files (kept up to date while the session runs).</summary>
    internal Dictionary<string, ManifestEntry> Manifest => _manifest;

    public async Task StartAsync(EditorViewModel editor, string userName, string address, int port)
    {
        if (IsActive)
            return;
        string root = AppServices.FileSystem.GetNativePath(editor.Project.Path) ?? throw new InvalidOperationException(Loc.T("Live Collaboration needs the project in a folder on this computer."));
        string? levelFull = editor.Document.FilePath is { } fp ? AppServices.FileSystem.GetNativePath(fp) : null;
        if (levelFull == null)
            throw new InvalidOperationException(Loc.T("Save the level in its project first, then start Live Collaboration."));
        string levelPath = CollabProjectFiles.Relative(root, levelFull);
        if (!CollabProjectFiles.IsSafe(root, levelPath))
            throw new InvalidOperationException(Loc.T("The level must be inside its project folder."));

        CollabProjectFiles.Recover(root);
        _project = editor.Project;
        _projectKey = CollabProjectLink.HostKey(root);
        _manifest = await CollabProjectFiles.BuildManifestAsync(root);
        _certificate = CollabCertificate.Create();
        _secret = RandomNumberGenerator.GetBytes(CollabCode.SecretLength);

        TcpListener listener;
        try
        {
            // IPv6 and IPv4 together where the system allows it.
            listener = new TcpListener(IPAddress.IPv6Any, port);
            listener.Server.DualMode = true;
            listener.Start();
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.AddressFamilyNotSupported or SocketError.ProtocolNotSupported or SocketError.ProtocolFamilyNotSupported)
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
        }
        _listener = listener;
        _hostCts = new CancellationTokenSource();
        _sequence = 0;
        _peers.Clear();
        _nextPeerId = 1;

        MyId = 0;
        Users.Clear();
        Users.Add(new CollabUser(0, SafeName(userName), RandomColor(0), true));
        Code = new CollabCode(address, port, CollabCertificate.Fingerprint(_certificate), _secret).ToString();
        Role = CollabRole.Host;
        AttachEditor(editor, root, levelPath);
        _sync!.AssignIds();
        _sync.Flush();
        _files = new CollabFileSync(this, root, levelPath, isHost: true);

        _ = AcceptLoopAsync(listener, _hostCts.Token);
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient tcp;
            try
            {
                tcp = await listener.AcceptTcpClientAsync(token);
            }
            catch
            {
                return;
            }
            _ = HandshakeAsync(tcp, token);
        }
    }

    private async Task HandshakeAsync(TcpClient tcp, CancellationToken token)
    {
        string ip = (tcp.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "?";
        // Too many wrong codes from one address: ignored for a while (guessing the secret is hopeless anyway).
        bool blocked = await Dispatcher.UIThread.InvokeAsync(() =>
            _failures.TryGetValue(ip, out var f) && f.Count >= 5 && DateTime.UtcNow - f.Since < TimeSpan.FromMinutes(5));
        if (blocked || _certificate == null)
        {
            tcp.Dispose();
            return;
        }
        CollabConnection connection;
        try
        {
            connection = await CollabConnection.AcceptAsync(tcp, _certificate, token);
        }
        catch
        {
            tcp.Dispose();
            return;
        }
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!IsHost)
            {
                connection.Close(null);
                return;
            }
            var peer = new Peer { Id = _nextPeerId++, Connection = connection };
            _peers[peer.Id] = peer;
            connection.Received += (c, m, b) => Dispatcher.UIThread.Post(() => HostReceive(peer, m, b));
            connection.Closed += (c, reason) => Dispatcher.UIThread.Post(() => HostPeerClosed(peer, reason));
            connection.Start();
        });
    }

    private void HostReceive(Peer peer, CollabMsg message, byte[] bytes)
    {
        if (!IsHost || !_peers.ContainsKey(peer.Id))
            return;
        try
        {
            switch (peer.State, message)
            {
                case (PeerState.Hello, HelloMsg hello):
                    OnHello(peer, hello);
                    break;
                case (PeerState.Syncing or PeerState.Active, FileRequestMsg request):
                    ServeFiles(peer, request.Paths, peer.State == PeerState.Syncing ? "join" : "update");
                    break;
                case (PeerState.Syncing, SyncedMsg):
                    ActivatePeer(peer);
                    break;
                case (PeerState.Active, OpsMsg ops):
                    _sync?.Flush();
                    _sync?.ApplyRemote(ops.Ops);
                    HostPublish(ops.Ops, peer.Id, ops.Batch, alreadyApplied: true);
                    break;
                case (PeerState.Active, PresenceMsg presence):
                    var fixedPresence = presence with { User = peer.Id };
                    OnPresence(fixedPresence);
                    BroadcastActive(fixedPresence, except: peer.Id);
                    break;
                case (PeerState.Active, ChangeRequestMsg request):
                    _files?.OnRequest(peer.Id, peer.Name, request);
                    break;
                case (PeerState.Active, FileChunkMsg chunk):
                    _files?.OnRequestChunk(peer.Id, chunk, bytes);
                    break;
                case (_, LeaveMsg):
                    peer.Connection.Close(null);
                    break;
                default:
                    // Anything else at this point is not allowed: the connection is ended.
                    _ = peer.Connection.CloseWithAsync(new ClosedMsg("invalid"));
                    break;
            }
        }
        catch (Exception ex)
        {
            Notice?.Invoke(Loc.F("Live Collaboration: {0}", ex.Message));
            _ = peer.Connection.CloseWithAsync(new ClosedMsg("invalid"));
        }
    }

    private void OnHello(Peer peer, HelloMsg hello)
    {
        string ip = peer.Connection.Remote;
        int colon = ip.LastIndexOf(':');
        if (colon > 0)
            ip = ip[..colon];
        bool secretOk = false;
        try
        {
            secretOk = CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(hello.Secret), _secret);
        }
        catch (FormatException)
        {
        }
        if (hello.Protocol != CollabWire.Protocol)
        {
            _ = peer.Connection.CloseWithAsync(new ClosedMsg("version"));
            return;
        }
        if (!secretOk)
        {
            var f = _failures.TryGetValue(ip, out var old) && DateTime.UtcNow - old.Since < TimeSpan.FromMinutes(5) ? old : (0, DateTime.UtcNow);
            _failures[ip] = (f.Item1 + 1, f.Item2);
            _ = peer.Connection.CloseWithAsync(new ClosedMsg("auth"));
            return;
        }
        if (_peers.Count > 16)
        {
            _ = peer.Connection.CloseWithAsync(new ClosedMsg("busy"));
            return;
        }
        peer.Name = SafeName(hello.UserName);
        peer.State = PeerState.Waiting;
        peer.Connection.Send(new PendingMsg());
        _approvals.Enqueue(peer);
        _ = ProcessApprovalsAsync();
    }

    /// <summary>Join requests are shown one at a time.</summary>
    private async Task ProcessApprovalsAsync()
    {
        if (_approving)
            return;
        _approving = true;
        try
        {
            while (_approvals.Count > 0)
            {
                var peer = _approvals.Dequeue();
                if (!IsHost || !peer.Connection.IsOpen)
                    continue;
                bool accept = await AppServices.Dialogs.ConfirmAsync(Loc.T("Join Request"),
                    Loc.F("{0} wants to join your Live Collaboration session.", peer.Name), Loc.T("Accept"), Loc.T("Decline"));
                if (!IsHost || !peer.Connection.IsOpen)
                    continue;
                if (!accept)
                {
                    _ = peer.Connection.CloseWithAsync(new ClosedMsg("declined"));
                    continue;
                }
                peer.State = PeerState.Syncing;
                peer.Color = RandomColor(peer.Id);
                peer.Connection.Send(new AcceptedMsg(peer.Id, _project!.Name, _projectKey, _editor!.Document.LevelName, _levelPath));
                // The whole project's list: the participant downloads only what it lacks or has differently.
                peer.Connection.Send(new ManifestMsg(_manifest.Values.ToList()));
            }
        }
        finally
        {
            _approving = false;
        }
    }

    private void ServeFiles(Peer peer, List<string> paths, string transfer)
    {
        var files = new List<ManifestEntry>();
        long total = 0;
        foreach (var path in paths.Distinct())
        {
            // Only files of the shared project, as listed: nothing else on this computer can be asked for.
            if (!_manifest.TryGetValue(path, out var entry) || !CollabProjectFiles.IsSafe(_projectRoot, path))
            {
                _ = peer.Connection.CloseWithAsync(new ClosedMsg("invalid"));
                return;
            }
            files.Add(entry);
            total += entry.Size;
        }
        var token = peer.Transfers.Token;
        string root = _projectRoot;
        _ = Task.Run(async () =>
        {
            try
            {
                await CollabProjectFiles.SendFilesAsync(peer.Connection, root, transfer, files, token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Dispatcher.UIThread.Post(() => Notice?.Invoke(Loc.F("Sending files to {0} failed: {1}", peer.Name, ex.Message)));
            }
        });
    }

    private void ActivatePeer(Peer peer)
    {
        _sync?.Flush();
        var snapshot = _sync?.Snapshot() ?? new List<LevelOp>();
        peer.Connection.Send(new LevelMsg(_sequence), Compress(snapshot));
        peer.State = PeerState.Active;
        Users.Add(new CollabUser(peer.Id, peer.Name, peer.Color, false));
        BroadcastUsers();
        // Where everyone already is.
        foreach (var presence in Presence.Values)
            peer.Connection.Send(presence);
        if (_lastPresence != null)
            peer.Connection.Send(_lastPresence);
        Notice?.Invoke(Loc.F("{0} joined the session.", peer.Name));
    }

    private void HostPublish(List<LevelOp> ops, int author, long batch, bool alreadyApplied = false)
    {
        _sequence++;
        var message = new OpsMsg(ops, _sequence, author, batch);
        BroadcastActive(message, except: -1);
    }

    private void BroadcastActive(CollabMsg message, int except, byte[]? bytes = null)
    {
        foreach (var peer in _peers.Values)
        {
            if (peer.State == PeerState.Active && peer.Id != except)
                peer.Connection.Send(message, bytes);
        }
    }

    internal void BroadcastFilesChanged(List<ManifestEntry> changed, List<string> removed)
    {
        if (changed.Count == 0 && removed.Count == 0)
            return;
        BroadcastActive(new FilesChangedMsg(changed, removed), except: -1);
    }

    internal void SendTo(int peerId, CollabMsg message)
    {
        if (_peers.TryGetValue(peerId, out var peer))
            peer.Connection.Send(message);
    }

    private void BroadcastUsers()
    {
        BroadcastActive(new UsersMsg(Users.ToList()), except: -1);
    }

    private void HostPeerClosed(Peer peer, string? reason)
    {
        if (!_peers.Remove(peer.Id))
            return;
        peer.Transfers.Cancel();
        _files?.PeerGone(peer.Id);
        var user = UserById(peer.Id);
        if (user != null)
        {
            Users.Remove(user);
            BroadcastUsers();
            Notice?.Invoke(Loc.F("{0} left the session.", peer.Name));
        }
        if (Presence.Remove(peer.Id))
        {
            PresenceChanged?.Invoke();
            BroadcastActive(new PresenceMsg(peer.Id, double.NaN, double.NaN, new List<long>(), new List<long>()), except: -1);
        }
    }

    public async Task KickAsync(int userId)
    {
        if (!IsHost || !_peers.TryGetValue(userId, out var peer))
            return;
        await peer.Connection.CloseWithAsync(new ClosedMsg("kicked"));
        HostPeerClosed(peer, "kicked");
    }

    private async Task StopHostingAsync()
    {
        var peers = _peers.Values.ToList();
        await Task.WhenAll(peers.Select(p => p.Connection.CloseWithAsync(new ClosedMsg("stopped"))));
        _peers.Clear();
        _hostCts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;
        _certificate?.Dispose();
        _certificate = null;
        _approvals.Clear();
    }

    // ================= PARTICIPANT ================= //

    private CollabConnection? _connection;
    private readonly List<(long Batch, List<LevelOp> Ops)> _pending = new();
    private long _nextBatch;
    private bool _ending;

    private void ParticipantSend(List<LevelOp> ops)
    {
        if (_connection == null)
            return;
        long batch = ++_nextBatch;
        _pending.Add((batch, ops));
        _connection.Send(new OpsMsg(ops, 0, MyId, batch));
    }

    /// <summary>Messages once the participant is in the level.</summary>
    private void ParticipantReceive(CollabMsg message, byte[] bytes)
    {
        if (!IsParticipant)
            return;
        try
        {
            switch (message)
            {
                case OpsMsg ops when ops.Author == MyId:
                    _pending.RemoveAll(p => p.Batch <= ops.Batch); // the host has it, in its place
                    break;
                case OpsMsg ops:
                    _sync?.Flush();
                    _sync?.ApplyRemote(ops.Ops);
                    // Own edits the host has not confirmed yet stay on top until it does.
                    if (_pending.Count > 0)
                        _sync?.Reapply(_pending.SelectMany(p => p.Ops));
                    break;
                case PresenceMsg presence when double.IsNaN(presence.X):
                    Presence.Remove(presence.User);
                    PresenceChanged?.Invoke();
                    break;
                case PresenceMsg presence:
                    OnPresence(presence);
                    break;
                case UsersMsg users:
                    Users.Clear();
                    foreach (var user in users.Users)
                        Users.Add(user);
                    foreach (var gone in Presence.Keys.Where(id => users.Users.All(u => u.Id != id)).ToList())
                        Presence.Remove(gone);
                    PresenceChanged?.Invoke();
                    break;
                case FilesChangedMsg changed:
                    _files?.OnHostFilesChanged(changed);
                    break;
                case FileChunkMsg chunk:
                    _files?.OnChunk(chunk, bytes);
                    break;
                case ChangeDecisionMsg decision:
                    _files?.OnDecision(decision);
                    break;
                case NoticeMsg notice:
                    Notice?.Invoke(notice.Text);
                    break;
                case ClosedMsg closed:
                    EndFromHost(closed.Reason);
                    break;
            }
        }
        catch (Exception ex)
        {
            Notice?.Invoke(Loc.F("Live Collaboration: {0}", ex.Message));
        }
    }

    internal void SendToHost(CollabMsg message) => _connection?.Send(message);

    internal Task SendFilesToHostAsync(string transfer, IEnumerable<ManifestEntry> files, CancellationToken token) =>
        _connection == null ? Task.CompletedTask : CollabProjectFiles.SendFilesAsync(_connection, _projectRoot, transfer, files, token);

    private void EndFromHost(string reason)
    {
        if (_ending || !IsParticipant)
            return;
        _ending = true;
        _connection?.Close(null);
        _connection = null;
        var editor = _editor;
        DetachEditor();
        Role = CollabRole.None;
        Code = null;
        Users.Clear();
        _pending.Clear();
        _ending = false;
        Ended?.Invoke(reason switch
        {
            "kicked" => Loc.T("The host removed you from the session."),
            "stopped" => Loc.T("The host ended the session."),
            _ => Loc.T("The connection to the host was lost."),
        }, editor);
    }

    // ================= ENDING ================= //

    /// <summary>Host: stops the session (everyone is disconnected). Participant: leaves it. The level and project stay.</summary>
    public async Task EndAsync()
    {
        if (!IsActive)
            return;
        if (IsHost)
        {
            await StopHostingAsync();
        }
        else if (_connection != null)
        {
            _ending = true;
            await _connection.CloseWithAsync(new LeaveMsg());
            _connection = null;
            _ending = false;
        }
        DetachEditor();
        Role = CollabRole.None;
        Code = null;
        Users.Clear();
        _pending.Clear();
    }

    /// <summary>The shared editor is closing (already confirmed): the session ends with it.</summary>
    public void EditorClosed(EditorViewModel editor)
    {
        if (ReferenceEquals(editor, _editor))
            _ = EndAsync();
    }
}

