using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Vectorier.Services.Collab;

/// <summary>
/// One encrypted link (TLS) between a participant and the host. Messages are sent in order; file chunks go through a
/// second, lower-priority queue so that level edits and presence are never stuck behind a large download. A ping keeps
/// the link checked: without anything heard for a while, it is treated as lost.
/// </summary>
public sealed class CollabConnection : IAsyncDisposable
{
    private static readonly TimeSpan PingEvery = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LostAfter = TimeSpan.FromSeconds(30);

    private readonly TcpClient _tcp;
    private readonly SslStream _ssl;
    private readonly Channel<byte[]> _urgent = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Channel<byte[]> _bulk = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource _cts = new();
    private DateTime _lastHeard = DateTime.UtcNow;
    private int _closed;

    private CollabConnection(TcpClient tcp, SslStream ssl, string remote)
    {
        _tcp = tcp;
        _ssl = ssl;
        Remote = remote;
    }

    public string Remote { get; }

    /// <summary>Raised on a background thread for every message.</summary>
    public event Action<CollabConnection, CollabMsg, byte[]>? Received;

    /// <summary>Raised once when the link ends (closed by either side, lost, or broken data).</summary>
    public event Action<CollabConnection, string?>? Closed;

    public bool IsOpen => Volatile.Read(ref _closed) == 0;

    // ================= OPENING ================= //

    /// <summary>Participant: connects to the host and checks its certificate against the Session ID's fingerprint.</summary>
    public static async Task<CollabConnection> ConnectAsync(CollabCode code, CancellationToken token)
    {
        var tcp = new TcpClient { NoDelay = true };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await tcp.ConnectAsync(code.Host, code.Port, timeout.Token);
            var ssl = new SslStream(tcp.GetStream(), false);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "vectorier-collab",
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                RemoteCertificateValidationCallback = (_, cert, _, _) => CollabCertificate.Matches(cert, code.Fingerprint),
            }, timeout.Token);
            return new CollabConnection(tcp, ssl, $"{code.Host}:{code.Port}");
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    /// <summary>Host: completes the encryption handshake of an incoming connection.</summary>
    public static async Task<CollabConnection> AcceptAsync(TcpClient tcp, X509Certificate2 certificate, CancellationToken token)
    {
        tcp.NoDelay = true;
        string remote = tcp.Client.RemoteEndPoint?.ToString() ?? "?";
        var ssl = new SslStream(tcp.GetStream(), false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ServerCertificate = certificate,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            ClientCertificateRequired = false,
        }, timeout.Token);
        return new CollabConnection(tcp, ssl, remote);
    }

    /// <summary>Starts reading and writing (after the event handlers are attached).</summary>
    public void Start()
    {
        _ = Task.Run(ReadLoopAsync);
        _ = Task.Run(WriteLoopAsync);
        _ = Task.Run(WatchLoopAsync);
    }

    // ================= SENDING ================= //

    public void Send(CollabMsg message, byte[]? bytes = null)
    {
        if (!IsOpen)
            return;
        if (_urgent.Writer.TryWrite(CollabWire.Pack(message, bytes ?? Array.Empty<byte>())))
            Interlocked.Increment(ref _queued);
    }

    /// <summary>Urgent messages queued but not written yet (closing waits for them).</summary>
    private int _queued;

    /// <summary>Large data (file chunks): waits while the queue is full, so a big transfer cannot fill the memory.</summary>
    public async Task SendBulkAsync(CollabMsg message, byte[] bytes, CancellationToken token)
    {
        if (!IsOpen)
            throw new IOException("The connection is closed.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _cts.Token);
        await _bulk.Writer.WriteAsync(CollabWire.Pack(message, bytes), linked.Token);
    }

    private async Task WriteLoopAsync()
    {
        var token = _cts.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (_urgent.Reader.TryRead(out var urgent))
                {
                    await _ssl.WriteAsync(urgent, token);
                    Interlocked.Decrement(ref _queued);
                    continue;
                }
                if (_bulk.Reader.TryRead(out var bulk))
                {
                    await _ssl.WriteAsync(bulk, token);
                    continue;
                }
                await _ssl.FlushAsync(token);
                // Wait for either queue (or the ping interval).
                var urgentWait = _urgent.Reader.WaitToReadAsync(token).AsTask();
                var bulkWait = _bulk.Reader.WaitToReadAsync(token).AsTask();
                var finished = await Task.WhenAny(urgentWait, bulkWait, Task.Delay(PingEvery, token));
                if (finished != urgentWait && finished != bulkWait)
                    _urgent.Writer.TryWrite(CollabWire.Pack(new PingMsg(), Array.Empty<byte>()));
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException or SocketException)
        {
            Close(token.IsCancellationRequested ? null : "lost");
        }
        catch (Exception ex)
        {
            Close(ex.Message);
        }
    }

    // ================= RECEIVING ================= //

    private async Task ReadLoopAsync()
    {
        var token = _cts.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var frame = await CollabWire.ReadAsync(_ssl, token);
                if (frame == null)
                    break;
                _lastHeard = DateTime.UtcNow;
                if (frame.Value.Message is PingMsg)
                    continue;
                Received?.Invoke(this, frame.Value.Message, frame.Value.Bytes);
            }
            Close("lost");
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException or SocketException)
        {
            Close(token.IsCancellationRequested ? null : "lost");
        }
        catch (Exception)
        {
            // Malformed data: the other side is not a well-behaved Vectorier.
            Close("invalid");
        }
    }

    private async Task WatchLoopAsync()
    {
        var token = _cts.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), token);
                if (DateTime.UtcNow - _lastHeard > LostAfter)
                {
                    Close("lost");
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // ================= CLOSING ================= //

    /// <summary>Sends a last message (best effort), then closes.</summary>
    public async Task CloseWithAsync(CollabMsg last)
    {
        if (!IsOpen)
            return;
        // Through the queue (one writer at a time on a TLS stream), then a short wait for it to go out.
        Send(last);
        var until = DateTime.UtcNow.AddSeconds(2);
        while (IsOpen && Volatile.Read(ref _queued) > 0 && DateTime.UtcNow < until)
            await Task.Delay(20);
        await Task.Delay(150);
        Close(null);
    }

    public void Close(string? reason)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return;
        _cts.Cancel();
        _urgent.Writer.TryComplete();
        _bulk.Writer.TryComplete();
        try { _ssl.Dispose(); } catch { }
        try { _tcp.Dispose(); } catch { }
        Closed?.Invoke(this, reason);
    }

    public ValueTask DisposeAsync()
    {
        Close(null);
        _cts.Dispose();
        return ValueTask.CompletedTask;
    }
}
