using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SpireParty.Net;

/// <summary>
/// Client side of the control channel: hello, then heartbeats until closed. Runs on background
/// threads; <see cref="HostLost"/> fires on one of them.
/// </summary>
public sealed class ControlClient : IDisposable
{
    readonly TcpClient _client;
    readonly NetworkStream _stream;
    readonly CancellationTokenSource _cts = new();
    readonly SemaphoreSlim _writeLock = new(1, 1);
    readonly TimeSpan _interval, _timeout;
    long _lastSeenTicks = DateTime.UtcNow.Ticks;
    int _ended;

    /// <summary>Reason: "host closed the party", "timeout", "connection lost"...</summary>
    public event Action<string>? HostLost;
    /// <summary>The host sent new seat names. Raised on a background thread.</summary>
    public event Action<Roster>? RosterReceived;

    /// <summary>Raised on a background thread when the host pauses or resumes the turn timer.</summary>
    public event Action<TimerPause>? TimerPauseReceived;

    public Welcome Welcome { get; }

    ControlClient(TcpClient client, NetworkStream stream, Welcome welcome, TimeSpan timeout)
    {
        _client = client;
        _stream = stream;
        Welcome = welcome;
        _interval = TimeSpan.FromSeconds(Math.Max(1, welcome.HeartbeatSeconds));
        _timeout = timeout;
    }

    public sealed record Result(ControlClient? Client, ControlMessage? Reply, string? Error);

    /// <summary>
    /// Connects and sends the hello. On welcome, returns a running client. On refuse or failure,
    /// returns null with the reply or a readable error.
    /// </summary>
    public static async Task<Result> ConnectAsync(string host, int port, Hello hello,
        TimeSpan? connectTimeout = null, TimeSpan? heartbeatTimeout = null, CancellationToken ct = default)
    {
        var client = new TcpClient { NoDelay = true };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(connectTimeout ?? TimeSpan.FromSeconds(10));
        try
        {
            await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
            var stream = client.GetStream();
            await ControlCodec.WriteAsync(stream, hello, timeout.Token).ConfigureAwait(false);
            var reply = await ControlCodec.ReadAsync(stream, timeout.Token).ConfigureAwait(false);
            if (reply is Welcome welcome)
            {
                var running = new ControlClient(client, stream, welcome, heartbeatTimeout ?? TimeSpan.FromSeconds(10));
                running.Start();
                return new Result(running, welcome, null);
            }
            client.Dispose();
            return new Result(null, reply, reply == null ? "The host closed the connection without answering." : null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            client.Dispose();
            return new Result(null, null, $"No answer from {host}:{port}. Check the address and that the host has the party open.");
        }
        catch (Exception e) when (e is SocketException or IOException or InvalidDataException)
        {
            client.Dispose();
            string why = e is SocketException se && se.SocketErrorCode == SocketError.ConnectionRefused
                ? "nothing is listening there (is the party open?)"
                : e.Message;
            return new Result(null, null, $"Could not reach {host}:{port}: {why}");
        }
    }

    void Start()
    {
        _ = ReadLoop();
        _ = HeartbeatLoop();
    }

    async Task ReadLoop()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var message = await ControlCodec.ReadAsync(_stream, _cts.Token).ConfigureAwait(false);
                if (message == null) { End("connection lost"); return; }
                Interlocked.Exchange(ref _lastSeenTicks, DateTime.UtcNow.Ticks);
                if (message is Bye bye) { End(bye.Reason.Length > 0 ? bye.Reason : "host closed the party"); return; }
                if (message is TimerPause pause)
                {
                    try { TimerPauseReceived?.Invoke(pause); }
                    catch (Exception e) when (e is not OperationCanceledException) { /* a handler bug must not drop the channel */ }
                }
                if (message is Roster roster)
                {
                    try { RosterReceived?.Invoke(roster); }
                    catch (Exception e) when (e is not OperationCanceledException) { /* a handler bug must not drop the channel */ }
                }
            }
        }
        catch (Exception e) when (e is IOException or SocketException or InvalidDataException or OperationCanceledException or ObjectDisposedException)
        {
            End("connection lost");
        }
    }

    async Task HeartbeatLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try { await Task.Delay(_interval, _cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            var silent = DateTime.UtcNow - new DateTime(Interlocked.Read(ref _lastSeenTicks), DateTimeKind.Utc);
            if (silent > _timeout) { End("timeout"); return; }
            await Send(new Heartbeat()).ConfigureAwait(false);
        }
    }

    async Task Send(ControlMessage message)
    {
        try
        {
            await _writeLock.WaitAsync(_cts.Token).ConfigureAwait(false);
            try { await ControlCodec.WriteAsync(_stream, message, _cts.Token).ConfigureAwait(false); }
            finally { _writeLock.Release(); }
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    void End(string reason)
    {
        if (Interlocked.Exchange(ref _ended, 1) == 1) return;
        _cts.Cancel();
        _client.Dispose();
        HostLost?.Invoke(reason);
    }

    /// <summary>Leaves on purpose: tells the host, so it is not treated as a drop.</summary>
    public void Dispose()
    {
        if (Volatile.Read(ref _ended) == 1) return;
        try { Send(new Bye("left")).Wait(TimeSpan.FromSeconds(1)); } catch { }
        Interlocked.Exchange(ref _ended, 1);
        _cts.Cancel();
        _client.Dispose();
    }
}
