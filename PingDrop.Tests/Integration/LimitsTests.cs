using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PingDrop.Abstractions;
using PingDrop.Tests.Unit;

namespace PingDrop.Tests.Integration;

public class LimitsTests
{
    private const string Base = "/pingdrop/stream/dashboard";

    private static async Task<HttpResponseMessage> RawConnectAsync(PingDropHost host, string? key)
    {
        string url = "/pingdrop/stream" + (key is null ? "" : "?uId=" + key);
        return await host.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).WaitAsync(TimeSpan.FromSeconds(15));
    }

    private static int ConnectedCount(PingDropHost host) { lock (host.Connected) return host.Connected.Count; }

    private static object Req(object payload, string targetType = "broadcast") =>
        new { targetType, targetId = (string?)null, eventName = "evt", payload };

    [Fact]
    public async Task PerKeyLimit_ThirdConnectionGets429_WithoutCallbackOrSseHeaders_OthersUnaffected()
    {
        await using var host = await PingDropHost.StartAsync(o => o.MaxConnectionsPerKey = 2);
        await using var a1 = await host.ConnectAsync("alice");
        await using var a2 = await host.ConnectAsync("alice");

        using var rejected = await RawConnectAsync(host, "alice");

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.Contains("Retry-After"));
        Assert.NotEqual("text/event-stream", rejected.Content.Headers.ContentType?.MediaType);
        Assert.Equal(2, ConnectedCount(host));

        await using var bob = await host.ConnectAsync("bob");
        await using var anon = await host.ConnectAsync();
        Assert.Equal(4, ConnectedCount(host));
    }

    [Fact]
    public async Task PerKeyLimit_SlotIsFreedAfterDisconnect()
    {
        await using var host = await PingDropHost.StartAsync(o => o.MaxConnectionsPerKey = 1);
        var store = host.Services.GetRequiredService<IPDConnectionStore>();
        var first = await host.ConnectAsync("alice");

        using (var rejected = await RawConnectAsync(host, "alice"))
            Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

        await first.DisposeAsync();
        await PingDropHost.WaitUntilAsync(() => store.CountByKey("alice") == 0);

        await using var again = await host.ConnectAsync("alice");
        Assert.Equal(1, store.CountByKey("alice"));
    }

    [Fact]
    public async Task TotalLimit_Returns503_AndKeylessConnectionsCountTowardIt()
    {
        await using var host = await PingDropHost.StartAsync(o =>
        {
            o.MaxTotalConnections = 2;
            o.MaxConnectionsPerKey = 1;
        });
        await using var anon1 = await host.ConnectAsync();
        await using var anon2 = await host.ConnectAsync();   // keyless: not limited per key

        using var rejectedAnon = await RawConnectAsync(host, null);
        using var rejectedKeyed = await RawConnectAsync(host, "carol");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, rejectedAnon.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, rejectedKeyed.StatusCode);
        Assert.True(rejectedAnon.Headers.Contains("Retry-After"));
        Assert.Equal(2, ConnectedCount(host));
    }

    [Fact]
    public async Task ConcurrentConnects_EndWithExactlyTheLimitRegistered()
    {
        await using var host = await PingDropHost.StartAsync(o => o.MaxTotalConnections = 5);
        var store = host.Services.GetRequiredService<IPDConnectionStore>();

        var tasks = Enumerable.Range(0, 30)
            .Select(i => host.Client.GetAsync("/pingdrop/stream?uId=u" + i, HttpCompletionOption.ResponseHeadersRead))
            .ToList();
        var responses = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(25, responses.Count(r => r.StatusCode == HttpStatusCode.ServiceUnavailable));
        Assert.Equal(5, store.Count);
        await PingDropHost.WaitUntilAsync(() => ConnectedCount(host) == 5);

        foreach (var r in responses) r.Dispose();
    }

    [Fact]
    public async Task Rejections_AreLoggedAsOneAggregatedWarning()
    {
        var logs = new LogCollector();
        await using var host = await PingDropHost.StartAsync(o => o.MaxTotalConnections = 1, configureLogging: b => b.AddProvider(logs));
        await using var first = await host.ConnectAsync("a");

        for (int i = 0; i < 5; i++)
        {
            using var r = await RawConnectAsync(host, "b");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        }

        var warnings = logs.Entries.Where(e => e.Level == LogLevel.Warning && e.Message.Contains("Refused a connection")).ToList();
        Assert.Single(warnings);
        Assert.Contains("MaxTotalConnections", warnings[0].Message);
    }

    [Fact]
    public async Task Shutdown_StillWinsOverLimits()
    {
        await using var host = await PingDropHost.StartAsync(o => o.MaxConnectionsPerKey = 1);
        await host.StopAsync();

        using var res = await RawConnectAsync(host, "alice");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
    }

    [Fact]
    public async Task Replay_StillWorksUnderLimits()
    {
        await using var host = await PingDropHost.StartAsync(o => o.MaxConnectionsPerKey = 1);
        await host.Service.BroadcastAsync(new { n = 1 }, "evt");
        await host.Service.BroadcastAsync(new { n = 2 }, "evt");

        await using var sse = await host.ConnectAsync("alice", lastEventIdHeader: "1");

        var (id, _, _) = await sse.ReadEventAsync();
        Assert.Equal("2", id);
    }

    [Fact]
    public async Task DashboardFeeds_HaveTheirOwnCap_AndDoNotCountTowardTotal()
    {
        await using var host = await PingDropHost.StartAsync(o =>
        {
            o.MaxTotalConnections = 1;
            o.Dashboard.MaxFeedConnections = 1;
        });
        await using var user = await host.ConnectAsync("alice");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var feed1 = await host.Client.GetAsync($"{Base}/sse", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        using var feed2 = await host.Client.GetAsync($"{Base}/sse", HttpCompletionOption.ResponseHeadersRead, cts.Token);

        Assert.Equal(HttpStatusCode.OK, feed1.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, feed2.StatusCode);
        Assert.True(feed2.Headers.Contains("Retry-After"));
        cts.Cancel();
    }

    [Fact]
    public async Task Dashboard_Send_OverPayloadLimit_Returns413_AndSmallPasses()
    {
        await using var host = await PingDropHost.StartAsync(o => o.MaxPayloadBytes = 32);
        await using var sse = await host.ConnectAsync("alice");

        var big = await host.Client.PostAsJsonAsync($"{Base}/send-notification", Req(new { text = new string('x', 100) }));
        var json = await big.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, big.StatusCode);
        Assert.False(json.GetProperty("isSuccess").GetBoolean());
        Assert.Contains("MaxPayloadBytes", json.GetProperty("message").GetString());

        var small = await host.Client.PostAsJsonAsync($"{Base}/send-notification", Req(new { a = 1 }));
        Assert.Equal(HttpStatusCode.OK, small.StatusCode);
        var (id, _, _) = await sse.ReadEventAsync();
        Assert.Equal("1", id);   // the rejected send consumed no id
    }

    [Fact]
    public async Task Service_OverPayloadLimit_Throws()
    {
        await using var host = await PingDropHost.StartAsync(o => o.MaxPayloadBytes = 8);

        await Assert.ThrowsAsync<PingDropPayloadTooLargeException>(async () => await host.Service.BroadcastAsync("far too long", "evt"));
    }

    [Fact]
    public async Task Dashboard_SendRateLimit_Returns429AfterN_AndRecoversAfterWindow()
    {
        var clock = new FakeTimeProvider();
        await using var host = await PingDropHost.StartAsync(
            o => o.Dashboard.MaxSendsPerMinute = 2,
            configureServices: s => s.AddSingleton<TimeProvider>(clock));

        for (int i = 0; i < 2; i++)
        {
            var ok = await host.Client.PostAsJsonAsync($"{Base}/send-notification", Req(new { i }));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        var limited = await host.Client.PostAsJsonAsync($"{Base}/send-notification", Req(new { i = 3 }));
        var json = await limited.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("60", limited.Headers.GetValues("Retry-After").Single());
        Assert.False(json.GetProperty("isSuccess").GetBoolean());

        clock.Advance(TimeSpan.FromSeconds(61));

        var again = await host.Client.PostAsJsonAsync($"{Base}/send-notification", Req(new { i = 4 }));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public async Task Dashboard_SendRateLimit_DoesNotLimitApplicationSendsOrOtherRoutes()
    {
        var clock = new FakeTimeProvider();
        await using var host = await PingDropHost.StartAsync(
            o => o.Dashboard.MaxSendsPerMinute = 1,
            configureServices: s => s.AddSingleton<TimeProvider>(clock));

        await host.Client.PostAsJsonAsync($"{Base}/send-notification", Req(new { i = 1 }));
        for (int i = 0; i < 5; i++) await host.Service.BroadcastAsync(new { i }, "evt");

        var metrics = await host.Client.GetAsync($"{Base}/metrics");
        Assert.Equal(HttpStatusCode.OK, metrics.StatusCode);
    }
}
