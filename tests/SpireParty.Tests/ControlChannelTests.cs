using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using SpireParty.Net;

namespace SpireParty.Tests;

public class ControlCodecTests
{
    static async Task<ControlMessage?> RoundTrip(ControlMessage m)
    {
        var ms = new MemoryStream();
        await ControlCodec.WriteAsync(ms, m, CancellationToken.None);
        ms.Position = 0;
        return await ControlCodec.ReadAsync(ms, CancellationToken.None);
    }

    [Fact]
    public async Task AllMessages_RoundTrip()
    {
        ControlMessage[] all =
        {
            new Hello("0.1.0", "v0.113.1", "d09d48e5", "ravi", "amber-otter-41"),
            new Welcome(7000002, "ravi", "gate", "amber-otter-41", "everything", 2),
            new Welcome(7000002, "ravi", "gate", null, "everything", 2),
            new Refuse("wrong_token", "Wrong token for seat \"ravi\"."),
            new Heartbeat(),
            new Bye("left"),
            new ChatSend("hello [b]party[/b] ✨"),
            new ChatBroadcast(7000002, "Ravi", "hello"),
        };
        foreach (var m in all) Assert.Equal(m, await RoundTrip(m));
    }

    [Fact]
    public async Task CleanEndOfStream_ReturnsNull_TruncatedFrameThrows()
    {
        Assert.Null(await ControlCodec.ReadAsync(new MemoryStream(), CancellationToken.None));
        var frame = ControlCodec.Encode(new Heartbeat());
        var truncated = new MemoryStream(frame[..^2]);
        await Assert.ThrowsAsync<EndOfStreamException>(() => ControlCodec.ReadAsync(truncated, CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ControlCodec.MaxFrameBytes + 1)]
    public async Task BadLength_Throws(int length)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, length);
        await Assert.ThrowsAsync<InvalidDataException>(() => ControlCodec.ReadAsync(new MemoryStream(header), CancellationToken.None));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"type\":\"launch-missiles\"}")]
    [InlineData("{\"type\":\"welcome\"}")]
    public void BadPayload_Throws(string json) =>
        Assert.Throws<InvalidDataException>(() => ControlCodec.Decode(System.Text.Encoding.UTF8.GetBytes(json)));
}

public class ListenAddressTests
{
    [Theory]
    [InlineData("100.64.0.1", true)]
    [InlineData("100.127.255.254", true)]
    [InlineData("100.128.0.1", false)]
    [InlineData("100.63.255.255", false)]
    [InlineData("192.168.1.10", false)]
    public void MeshRange(string ip, bool mesh) => Assert.Equal(mesh, ListenAddresses.IsMeshAddress(IPAddress.Parse(ip)));

    [Fact]
    public void Auto_BindsMeshAndLoopback_AcceptsOnlyThose()
    {
        var plan = ListenAddresses.Resolve("auto", new[] { IPAddress.Parse("192.168.1.10"), IPAddress.Parse("100.101.102.103") });
        Assert.Equal(new[] { IPAddress.Parse("100.101.102.103"), IPAddress.Loopback }, plan.BindAddresses);
        Assert.True(plan.IsAllowedSource(IPAddress.Parse("100.70.1.2")));
        Assert.True(plan.IsAllowedSource(IPAddress.Loopback));
        Assert.False(plan.IsAllowedSource(IPAddress.Parse("192.168.1.20")));
        Assert.False(plan.IsAllowedSource(IPAddress.Parse("8.8.8.8")));
    }

    [Fact]
    public void Auto_WithoutMesh_IsLoopbackOnly() =>
        Assert.Equal(new[] { IPAddress.Loopback }, ListenAddresses.Resolve("auto", Array.Empty<IPAddress>()).BindAddresses);
}

public class ControlServerTests
{
    static readonly ControlServer.Options Fast = new(
        HelloTimeout: TimeSpan.FromSeconds(2), HeartbeatInterval: TimeSpan.FromMilliseconds(100),
        HeartbeatTimeout: TimeSpan.FromMilliseconds(600), GrantLifetime: TimeSpan.FromSeconds(5));

    static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    static ControlServer StartServer(int port, ControlServer.HelloHandler handler)
    {
        var plan = ListenAddresses.Resolve("auto", Array.Empty<IPAddress>());
        var server = new ControlServer(plan, port, handler, Fast);
        server.Start();
        return server;
    }

    static ControlMessage AdmitRavi(Hello h, IPAddress from, IReadOnlyCollection<string> connected) =>
        h.Token == "good-token-11"
            ? new Welcome(7000002, h.SeatId, "gate", null, "everything", 1)
            : new Refuse("wrong_token", "Wrong token.");

    static Hello Hello(string token) => new("0.1.0", "v0.113.1", "d09d48e5", "ravi", token);

    [Fact]
    public async Task Welcome_GrantsAndConnects_LeaveIsReportedAsLeft()
    {
        int port = FreePort();
        using var server = StartServer(port, AdmitRavi);
        var lost = new TaskCompletionSource<string>();
        server.SeatLost += (_, reason) => lost.TrySetResult(reason);

        var result = await ControlClient.ConnectAsync("127.0.0.1", port, Hello("good-token-11"));
        Assert.NotNull(result.Client);
        Assert.Equal(7000002UL, result.Client!.Welcome.PlayerId);
        await WaitUntil(() => server.ConnectedSeatIds.Contains("ravi"));
        Assert.True(server.HasGrant(IPAddress.Loopback, 7000002));
        Assert.False(server.HasGrant(IPAddress.Loopback, 7000003));

        result.Client.Dispose();
        Assert.Equal("left", await lost.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Empty(server.ConnectedSeatIds);
    }

    [Fact]
    public async Task Refuse_IsReturnedToClient_NoSession()
    {
        int port = FreePort();
        using var server = StartServer(port, AdmitRavi);
        var result = await ControlClient.ConnectAsync("127.0.0.1", port, Hello("bad"));
        Assert.Null(result.Client);
        Assert.Equal("wrong_token", Assert.IsType<Refuse>(result.Reply).Code);
        Assert.Empty(server.ConnectedSeatIds);
    }

    [Fact]
    public async Task SilentClient_IsDroppedAfterHeartbeatTimeout()
    {
        int port = FreePort();
        using var server = StartServer(port, AdmitRavi);
        var lost = new TaskCompletionSource<string>();
        server.SeatLost += (_, reason) => lost.TrySetResult(reason);

        // A raw client that says hello and then goes silent (like a frozen game).
        using var raw = new TcpClient();
        await raw.ConnectAsync(IPAddress.Loopback, port);
        await ControlCodec.WriteAsync(raw.GetStream(), Hello("good-token-11"), CancellationToken.None);
        Assert.IsType<Welcome>(await ControlCodec.ReadAsync(raw.GetStream(), CancellationToken.None));

        Assert.Equal("timeout", await lost.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Chat_ClientSendsToHost_HostBroadcastReachesClient()
    {
        int port = FreePort();
        using var server = StartServer(port, AdmitRavi);
        var atHost = new TaskCompletionSource<(ControlSeat, ControlMessage)>();
        server.MessageReceived += (seat, m) => atHost.TrySetResult((seat, m));

        var result = await ControlClient.ConnectAsync("127.0.0.1", port, Hello("good-token-11"));
        var atClient = new TaskCompletionSource<ChatBroadcast>();
        result.Client!.ChatReceived += c => atClient.TrySetResult(c);
        await WaitUntil(() => server.ConnectedSeatIds.Count == 1);

        await result.Client.SendAsync(new ChatSend("gg"));
        var (seat, message) = await atHost.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("ravi", seat.SeatId);
        Assert.Equal("gg", Assert.IsType<ChatSend>(message).Text);

        server.Broadcast(new ChatBroadcast(seat.PlayerId, "Ravi", "gg"));
        var echoed = await atClient.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new ChatBroadcast(7000002, "Ravi", "gg"), echoed);
        result.Client.Dispose();
    }

    [Fact]
    public async Task HostClosing_TellsClientWithReason()
    {
        int port = FreePort();
        var server = StartServer(port, AdmitRavi);
        var result = await ControlClient.ConnectAsync("127.0.0.1", port, Hello("good-token-11"));
        var hostLost = new TaskCompletionSource<string>();
        result.Client!.HostLost += r => hostLost.TrySetResult(r);
        await WaitUntil(() => server.ConnectedSeatIds.Count == 1);

        server.Dispose();
        Assert.Equal("host closed the party", await hostLost.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task NothingListening_GivesReadableError()
    {
        var result = await ControlClient.ConnectAsync("127.0.0.1", FreePort(), Hello("x"), TimeSpan.FromSeconds(3));
        Assert.Null(result.Client);
        Assert.Contains("is the party open", result.Error);
    }

    static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            await Task.Delay(20);
        }
    }
}
