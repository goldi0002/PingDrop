namespace PingDrop.Tests.Integration;

public class SseEndpointTests
{
    [Fact]
    public async Task Stream_ReturnsEventStreamHeaders()
    {
        await using var host = await PingDropHost.StartAsync();
        await using var sse = await host.ConnectAsync();

        var response = await sse.GetResponseAsync();

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-cache", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Broadcast_IsReceivedByConnectedClient()
    {
        await using var host = await PingDropHost.StartAsync();
        await using var sse = await host.ConnectAsync();

        await host.Service.BroadcastAsync(new { Message = "hello" }, "notification");
        var (id, evt, data) = await sse.ReadEventAsync();

        Assert.NotNull(id);
        Assert.Equal("notification", evt);
        Assert.Equal("{\"Message\":\"hello\"}", data);
    }

    [Fact]
    public async Task SendTo_IsReceivedOnlyByTargetKey()
    {
        await using var host = await PingDropHost.StartAsync();
        await using var alice = await host.ConnectAsync("alice");
        await using var bob = await host.ConnectAsync("bob");

        await host.Service.SendToAsync("alice", "for-alice", "private");
        await host.Service.SendToAsync("bob", "for-bob", "private");

        Assert.Equal("\"for-alice\"", (await alice.ReadEventAsync()).Data);
        Assert.Equal("\"for-bob\"", (await bob.ReadEventAsync()).Data);
    }

    [Fact]
    public async Task SendTo_ReachesAllConnectionsOfSameKey()
    {
        await using var host = await PingDropHost.StartAsync();
        await using var tab1 = await host.ConnectAsync("alice");
        await using var tab2 = await host.ConnectAsync("alice");

        await host.Service.SendToAsync("alice", "x", "evt");

        Assert.Equal("\"x\"", (await tab1.ReadEventAsync()).Data);
        Assert.Equal("\"x\"", (await tab2.ReadEventAsync()).Data);
    }

    [Fact]
    public async Task EventTypeFilter_SkipsNonMatchingEvents()
    {
        await using var host = await PingDropHost.StartAsync();
        await using var sse = await host.ConnectAsync(eventTypes: "wanted");

        await host.Service.BroadcastAsync("skip", "unwanted");
        await host.Service.BroadcastAsync("keep", "wanted");

        var (_, evt, data) = await sse.ReadEventAsync();
        Assert.Equal("wanted", evt);
        Assert.Equal("\"keep\"", data);
    }

    [Fact]
    public async Task Disconnect_RunsCallback_AndRemovesClient()
    {
        await using var host = await PingDropHost.StartAsync();
        var sse = await host.ConnectAsync("alice");
        Assert.Single(host.Connected);

        await sse.DisposeAsync();

        await PingDropHost.WaitUntilAsync(() => { lock (host.Disconnected) return host.Disconnected.Count == 1; });
        Assert.Equal(host.Connected[0], host.Disconnected[0]);
    }

    [Fact]
    public async Task CustomEndpointPath_IsHonored()
    {
        await using var host = await PingDropHost.StartAsync(o => o.EndpointPath = "/custom/stream");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var pending = host.Client.GetAsync("/custom/stream", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        using var response = await pending;

        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Connect_FlushesRetryHintAndConnectedComment_BeforeAnyEvent()
    {
        await using var host = await PingDropHost.StartAsync(o => o.ClientRetryInterval = TimeSpan.FromSeconds(5));
        await using var sse = await host.ConnectAsync();

        var response = await sse.GetResponseAsync();
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("retry: 5000", await sse.ReadLineAsync());
        Assert.Equal(": connected", await sse.ReadLineAsync());
    }

    [Fact]
    public async Task Connect_OmitsRetryHint_WhenDisabled()
    {
        await using var host = await PingDropHost.StartAsync(o => o.ClientRetryInterval = TimeSpan.Zero);
        await using var sse = await host.ConnectAsync();

        Assert.Equal(": connected", await sse.ReadLineAsync());
    }

    [Fact]
    public async Task Stream_KeepsCacheAndProxyHeaders()
    {
        await using var host = await PingDropHost.StartAsync();
        await using var sse = await host.ConnectAsync();

        var response = await sse.GetResponseAsync();
        Assert.Equal("no", response.Headers.GetValues("X-Accel-Buffering").Single());
        Assert.Contains("no-transform", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Heartbeat_IsSentWhileIdle_AndDoesNotBreakEventParsing()
    {
        await using var host = await PingDropHost.StartAsync(o => o.HeartbeatInterval = TimeSpan.FromMilliseconds(100));
        await using var sse = await host.ConnectAsync();

        await sse.ReadUntilLineAsync(": connected");
        await sse.ReadUntilLineAsync(": ping");
        await sse.ReadUntilLineAsync(": ping");

        await host.Service.BroadcastAsync("x", "evt");
        var (_, evt, _) = await sse.ReadEventAsync();
        Assert.Equal("evt", evt);
    }

    [Fact]
    public async Task Heartbeat_IsNotCountedAsDelivery()
    {
        await using var host = await PingDropHost.StartAsync(o => o.HeartbeatInterval = TimeSpan.FromMilliseconds(50));
        await using var sse = await host.ConnectAsync();

        await sse.ReadUntilLineAsync(": ping");
        await sse.ReadUntilLineAsync(": ping");

        Assert.Equal(0, ((PingDrop.Internal.PingDropHub)host.Service).GetMetrics().SuccessfulDeliveries);
    }

    [Fact]
    public async Task Heartbeat_CanBeDisabled()
    {
        await using var host = await PingDropHost.StartAsync(o => o.HeartbeatInterval = TimeSpan.Zero);
        await using var sse = await host.ConnectAsync();
        await sse.ReadUntilLineAsync(": connected");
        Assert.Equal("", await sse.ReadLineAsync());

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            await sse.ReadLineAsync().WaitAsync(cts.Token);
        });
    }

    [Fact]
    public async Task SendToMany_IsReceivedByEveryListedUser()
    {
        await using var host = await PingDropHost.StartAsync();
        await using var alice = await host.ConnectAsync("alice");
        await using var bob = await host.ConnectAsync("bob");
        await using var carol = await host.ConnectAsync("carol");

        await host.Service.SendToAsync(new[] { "alice", "bob" }, "multi", "evt");
        await host.Service.SendToAsync("carol", "solo", "evt");

        var a = await alice.ReadEventAsync();
        var b = await bob.ReadEventAsync();
        Assert.Equal("\"multi\"", a.Data);
        Assert.Equal("\"multi\"", b.Data);
        Assert.Equal(a.Id, b.Id);
        Assert.Equal("\"solo\"", (await carol.ReadEventAsync()).Data);
    }

    [Fact]
    public async Task DisconnectClient_WhenQueueFull_EndsStream_AndFiresClientDisconnected()
    {
        await using var host = await PingDropHost.StartAsync(o =>
        {
            o.ClientQueueCapacity = 2;
            o.ClientQueueFullMode = PingDrop.Abstractions.PingDropQueueFullMode.DisconnectClient;
        });
        await using var sse = await host.ConnectAsync("slow");

        // The client never reads, so large events back up in the response pipe and then in the queue.
        var payload = new string('x', 256 * 1024);
        for (int i = 0; i < 50; i++)
            await host.Service.BroadcastAsync(new { payload }, "big");

        // Draining the stream reaches its end: the server completed it once the queue overflowed.
        await Assert.ThrowsAsync<EndOfStreamException>(async () =>
        {
            while (true) await sse.ReadLineAsync();
        });

        await PingDropHost.WaitUntilAsync(() => { lock (host.Disconnected) return host.Disconnected.Count == 1; });
    }
}
