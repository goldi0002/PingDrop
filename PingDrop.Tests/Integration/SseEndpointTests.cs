namespace PingDrop.Tests.Integration;

public class SseEndpointTests
{
    [Fact]
    public async Task Stream_ReturnsEventStreamHeaders()
    {
        await using var host = await PingDropHost.StartAsync();
        await using var sse = await host.ConnectAsync();

        // Headers are only sent with the first event (see heartbeat issue), so send one.
        await host.Service.BroadcastAsync("x", "evt");
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
        await PingDropHost.WaitUntilAsync(() => { lock (host.Connected) return host.Connected.Count == 1; });
        await host.Service.BroadcastAsync("x", "evt");
        using var response = await pending;

        Assert.True(response.IsSuccessStatusCode);
    }
}
