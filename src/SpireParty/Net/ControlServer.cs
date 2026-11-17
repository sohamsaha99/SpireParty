using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SpireParty.Net;

/// <summary>A seat that passed the hello and is connected to the control channel.</summary>
public sealed record ControlSeat(string SeatId, ulong PlayerId, IPAddress Address);

/// <summary>
/// Host side of the control channel. Runs on background threads: callers must marshal events
/// to the game's main thread themselves. Never throws out of its loops.
/// </summary>
public sealed class ControlServer : IDisposable
{
    public sealed record Options(TimeSpan HelloTimeout, TimeSpan HeartbeatInterval, TimeSpan HeartbeatTimeout, TimeSpan GrantLifetime)
    {
        public static Options Default => new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));
    }

    /// <summary>Decides a hello. Returns <see cref="Welcome"/> or <see cref="Refuse"/>. Called on a background thread.</summary>
    public delegate ControlMessage HelloHandler(Hello hello, IPAddress from, IReadOnlyCollection<string> connectedSeatIds);

    readonly ListenPlan _plan;
    readonly HelloHandler _onHello;
    readonly Options _options;
    readonly Action<string> _log;
    readonly CancellationTokenSource _cts = new();
    readonly List<TcpListener> _listeners = new();
    readonly ConcurrentDictionary<string, Session> _sessions = new();
    readonly ConcurrentDictionary<(string Address, ulong PlayerId), DateTime> _grants = new();
    readonly object _helloLock = new();

    public event Action<ControlSeat>? SeatConnected;
    /// <summary>Seat and reason ("left", "timeout", "kicked", "error").</summary>
    public event Action<ControlSeat, string>? SeatLost;

    public int Port { get; }

    sealed class Session(ControlSeat seat, TcpClient client, NetworkStream stream)
    {
        public ControlSeat Seat { get; } = seat;
        public TcpClient Client { get; } = client;
        public NetworkStream Stream { get; } = stream;
        public long LastSeenTicks = DateTime.UtcNow.Ticks;
        public readonly SemaphoreSlim WriteLock = new(1, 1);
        public int Ended;
    }

    public ControlServer(ListenPlan plan, int port, HelloHandler onHello, Options? options = null, Action<string>? log = null)
    {
        _plan = plan;
        _onHello = onHello;
        _options = options ?? Options.Default;
        _log = log ?? (_ => { });
        Port = port;
    }

    /// <summary>Binds every address in the plan. Throws if none could be bound.</summary>
    public void Start()
    {
        var errors = new List<string>();
        foreach (var address in _plan.BindAddresses)
        {
            try
            {
                var listener = new TcpListener(address, Port);
                listener.Start();
                _listeners.Add(listener);
                _ = AcceptLoop(listener);
            }
            catch (SocketException e)
            {
                errors.Add($"{address}:{Port} ({e.SocketErrorCode})");
            }
        }
        if (_listeners.Count == 0)
            throw new IOException($"Could not open the SpireParty control port: {string.Join(", ", errors)}");
        if (errors.Count > 0) _log($"control channel: could not bind {string.Join(", ", errors)}");
        _ = MonitorLoop();
        _log($"control channel listening on {_plan.Description}, TCP {Port}");
    }

    public IReadOnlyCollection<string> ConnectedSeatIds => _sessions.Keys.ToList();
    public IReadOnlyCollection<ControlSeat> ConnectedSeats => _sessions.Values.Select(s => s.Seat).ToList();

    /// <summary>
    /// True if <paramref name="playerId"/> was welcomed from <paramref name="address"/> recently.
    /// The game-side peer check uses this to bind ENet peers to seats.
    /// </summary>
    public bool HasGrant(IPAddress address, ulong playerId)
    {
        var key = (Normalize(address).ToString(), playerId);
        return _grants.TryGetValue(key, out var expires) && expires > DateTime.UtcNow;
    }

    /// <summary>Is this seat's control connection alive (used to accept ENet rejoins)?</summary>
    public bool IsConnected(ulong playerId, IPAddress address) =>
        _sessions.Values.Any(s => s.Seat.PlayerId == playerId && Normalize(s.Seat.Address).Equals(Normalize(address)));

    public void Kick(string seatId, string reason)
    {
        if (_sessions.TryGetValue(seatId, out var session))
            _ = EndSession(session, "kicked", new Bye(reason));
    }

    async Task AcceptLoop(TcpListener listener)
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException e) { _log($"control channel: accept failed ({e.SocketErrorCode})"); continue; }

            var remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;
            if (remote == null || !_plan.IsAllowedSource(remote))
            {
                _log($"control channel: dropped connection from {remote} (not a mesh or local address)");
                client.Dispose();
                continue;
            }
            _ = HandleClient(client, Normalize(remote));
        }
    }

    async Task HandleClient(TcpClient client, IPAddress remote)
    {
        Session? session = null;
        try
        {
            client.NoDelay = true;
            var stream = client.GetStream();

            using var helloCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            helloCts.CancelAfter(_options.HelloTimeout);
            var first = await ControlCodec.ReadAsync(stream, helloCts.Token).ConfigureAwait(false);
            if (first is not Hello hello)
            {
                _log($"control channel: {remote} sent no hello");
                client.Dispose();
                return;
            }

            ControlMessage reply;
            lock (_helloLock) reply = _onHello(hello, remote, ConnectedSeatIds);

            await ControlCodec.WriteAsync(stream, reply, _cts.Token).ConfigureAwait(false);
            if (reply is not Welcome welcome)
            {
                _log($"control channel: refused {remote} seat \"{hello.SeatId}\": {(reply as Refuse)?.Code}");
                client.Dispose();
                return;
            }

            var seat = new ControlSeat(welcome.SeatId, welcome.PlayerId, remote);
            session = new Session(seat, client, stream);
            if (!_sessions.TryAdd(seat.SeatId, session))
            {
                await ControlCodec.WriteAsync(stream, new Bye("seat already connected"), _cts.Token).ConfigureAwait(false);
                client.Dispose();
                return;
            }
            _grants[(remote.ToString(), seat.PlayerId)] = DateTime.UtcNow + _options.GrantLifetime;
            SeatConnected?.Invoke(seat);

            while (!_cts.IsCancellationRequested)
            {
                var message = await ControlCodec.ReadAsync(stream, _cts.Token).ConfigureAwait(false);
                if (message == null) { await EndSession(session, "left", null).ConfigureAwait(false); return; }
                Interlocked.Exchange(ref session.LastSeenTicks, DateTime.UtcNow.Ticks);
                if (message is Bye) { await EndSession(session, "left", null).ConfigureAwait(false); return; }
            }
        }
        catch (Exception e) when (e is IOException or SocketException or InvalidDataException or OperationCanceledException or ObjectDisposedException)
        {
            if (session != null) await EndSession(session, _cts.IsCancellationRequested ? "host stopped" : "error", null).ConfigureAwait(false);
            else client.Dispose();
        }
    }

    async Task MonitorLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try { await Task.Delay(_options.HeartbeatInterval, _cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            foreach (var key in _grants.Where(g => g.Value <= DateTime.UtcNow).Select(g => g.Key).ToList())
                _grants.TryRemove(key, out _);

            foreach (var session in _sessions.Values.ToList())
            {
                var silent = DateTime.UtcNow - new DateTime(Interlocked.Read(ref session.LastSeenTicks), DateTimeKind.Utc);
                if (silent > _options.HeartbeatTimeout)
                {
                    _ = EndSession(session, "timeout", null);
                    continue;
                }
                _ = Send(session, new Heartbeat());
            }
        }
    }

    async Task Send(Session session, ControlMessage message)
    {
        try
        {
            await session.WriteLock.WaitAsync(_cts.Token).ConfigureAwait(false);
            try { await ControlCodec.WriteAsync(session.Stream, message, _cts.Token).ConfigureAwait(false); }
            finally { session.WriteLock.Release(); }
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            // The read loop or the monitor will notice the dead connection.
        }
    }

    async Task EndSession(Session session, string reason, ControlMessage? last)
    {
        if (Interlocked.Exchange(ref session.Ended, 1) == 1) return;
        _sessions.TryRemove(new KeyValuePair<string, Session>(session.Seat.SeatId, session));
        _grants.TryRemove((session.Seat.Address.ToString(), session.Seat.PlayerId), out _);
        if (last != null) await Send(session, last).ConfigureAwait(false);
        session.Client.Dispose();
        _log($"control channel: seat \"{session.Seat.SeatId}\" lost ({reason})");
        try { SeatLost?.Invoke(session.Seat, reason); }
        catch (Exception e) { _log($"control channel: SeatLost handler threw {e}"); }
    }

    static IPAddress Normalize(IPAddress a) => a.IsIPv4MappedToIPv6 ? a.MapToIPv4() : a;

    public void Dispose()
    {
        if (_cts.IsCancellationRequested) return;
        // Tell clients the host left on purpose (so they don't treat it as a drop), then close.
        var goodbyes = _sessions.Values.ToList()
            .Select(s => EndSession(s, "host stopped", new Bye("host closed the party"))).ToArray();
        try { Task.WaitAll(goodbyes, TimeSpan.FromSeconds(1)); } catch (AggregateException) { }
        _cts.Cancel();
        foreach (var listener in _listeners) listener.Stop();
    }
}
