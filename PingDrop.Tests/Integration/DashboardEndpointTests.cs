using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PingDrop.Tests.Integration;

public class DashboardEndpointTests
{
    private const string Base = "/pingdrop/stream/dashboard";

    private static object Req(string targetType, string? targetId = null, string eventName = "evt") =>
        new { targetType, targetId, eventName, payload = new { hello = "world" } };

    [Fact]
    public async Task Metrics_ReturnsSuccessEnvelope()
    {
        await using var host = await PingDropHost.StartAsync();

        var res = await host.Client.GetAsync($"{Base}/metrics");
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.True(json.GetProperty("isSuccess").GetBoolean());
    }

    [Theory]
    [InlineData("", "text/html; charset=utf-8")]
    [InlineData("/app.js", "text/javascript; charset=utf-8")]
    [InlineData("/app.css", "text/css; charset=utf-8")]
    public async Task DashboardFiles_ServedWithExpectedContentType(string suffix, string expected)
    {
        await using var host = await PingDropHost.StartAsync();

        var res = await host.Client.GetAsync($"{Base}{suffix}");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(expected, res.Content.Headers.ContentType?.ToString());
    }

    [Fact]
    public async Task About_ReturnsSuccessEnvelope()
    {
        await using var host = await PingDropHost.StartAsync();

        var res = await host.Client.GetAsync($"{Base}/about");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Dashboard_Disabled_DoesNotMapRoutes()
    {
        await using var host = await PingDropHost.StartAsync(o => o.EnableDashboard = false);

        var res = await host.Client.GetAsync($"{Base}/metrics");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Events_ListsRecentEventsNewestFirst_WithoutPayloadByDefault()
    {
        await using var host = await PingDropHost.StartAsync();
        await host.Service.BroadcastAsync(new { secret = "s3cret" }, "first");
        await host.Service.SendToAsync("alice", new { secret = "s3cret" }, "second");

        var res = await host.Client.GetAsync($"{Base}/events");
        var text = await res.Content.ReadAsStringAsync();
        var data = JsonDocument.Parse(text).RootElement.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(2, data.GetArrayLength());
        Assert.Equal("second", data[0].GetProperty("eventType").GetString());
        Assert.Equal("alice", data[0].GetProperty("target").GetString());
        Assert.False(data[1].TryGetProperty("target", out _));
        Assert.True(data[0].GetProperty("sizeBytes").GetInt32() > 0);
        Assert.DoesNotContain("s3cret", text);
    }

    [Fact]
    public async Task Events_ShowsPayload_WhenEnabled_AndHonorsLimit()
    {
        await using var host = await PingDropHost.StartAsync(o => o.Dashboard.ShowEventPayloads = true);
        for (int i = 0; i < 3; i++) await host.Service.BroadcastAsync(new { n = i }, "tick");

        var q = Uri.EscapeDataString("{\"limit\":2}");
        var res = await host.Client.GetAsync($"{Base}/events?q={q}");
        var data = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");

        Assert.Equal(2, data.GetArrayLength());
        Assert.Contains("\"n\":2", data[0].GetProperty("payload").GetString());
    }

    [Fact]
    public async Task Connections_ListsConnectedClient()
    {
        await using var host = await PingDropHost.StartAsync();
        await using var sse = await host.ConnectAsync("alice");

        var res = await host.Client.GetAsync($"{Base}/connections");
        var text = await res.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("alice", text);
    }

    [Fact]
    public async Task SendNotification_Broadcast_ReachesClient()
    {
        await using var host = await PingDropHost.StartAsync();
        await using var sse = await host.ConnectAsync("alice");

        var res = await host.Client.PostAsJsonAsync($"{Base}/send-notification", Req("broadcast"));
        var (_, evt, data) = await sse.ReadEventAsync();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("evt", evt);
        Assert.Equal("{\"hello\":\"world\"}", data);
    }

    [Fact]
    public async Task SendNotification_ToKey_ReachesThatClient()
    {
        await using var host = await PingDropHost.StartAsync();
        await using var sse = await host.ConnectAsync("alice");

        var res = await host.Client.PostAsJsonAsync($"{Base}/send-notification", Req("key", "alice"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("evt", (await sse.ReadEventAsync()).Event);
    }

    [Fact]
    public async Task SendNotification_ToDashboardKey_IsForbidden()
    {
        await using var host = await PingDropHost.StartAsync();

        var res = await host.Client.PostAsJsonAsync($"{Base}/send-notification", Req("key", "__pingdrop_dashboard__"));

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task SendNotification_UnknownConnection_IsNotFound()
    {
        await using var host = await PingDropHost.StartAsync();

        var res = await host.Client.PostAsJsonAsync($"{Base}/send-notification", Req("connection", "999999"));

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Theory]
    [InlineData("bogus", "x")]
    [InlineData("", "x")]
    public async Task SendNotification_InvalidTargetType_IsBadRequest(string targetType, string id)
    {
        await using var host = await PingDropHost.StartAsync();

        var res = await host.Client.PostAsJsonAsync($"{Base}/send-notification", Req(targetType, id));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task SendNotification_KeyWithoutTargetId_IsBadRequest()
    {
        await using var host = await PingDropHost.StartAsync();

        var res = await host.Client.PostAsJsonAsync($"{Base}/send-notification", Req("key"));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    private static async Task<SseStream> ConnectDashboardAsync(PingDropHost host)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var task = host.Client.GetAsync($"{Base}/sse", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        await task.WaitAsync(TimeSpan.FromSeconds(5));
        return new SseStream(task, cts);
    }

    [Fact]
    public async Task DashboardConnection_IsExcludedFromMetricsAndConnections()
    {
        await using var host = await PingDropHost.StartAsync();
        await using var dash = await ConnectDashboardAsync(host);

        var conns = await host.Client.GetStringAsync($"{Base}/connections");
        var metrics = await host.Client.GetFromJsonAsync<JsonElement>($"{Base}/metrics");
        var connected = metrics.GetProperty("data").EnumerateArray()
            .First(m => m.GetProperty("label").GetString() == "Connected Clients").GetProperty("value").GetString();

        Assert.DoesNotContain("__pingdrop_dashboard__", conns);
        Assert.Equal("0", connected);
    }

    [Fact]
    public async Task DashboardFeed_StillReceivesMetricsAndLogEvents()
    {
        await using var host = await PingDropHost.StartAsync();
        await using var alice = await host.ConnectAsync("alice");
        await using var dash = await ConnectDashboardAsync(host);

        var res = await host.Client.PostAsJsonAsync($"{Base}/send-notification", Req("broadcast"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var seen = new HashSet<string>();
        for (int i = 0; i < 6 && !(seen.Contains("metrics")); i++)
        {
            var (_, evt, _) = await dash.ReadEventAsync();
            seen.Add(evt!);
            Assert.NotEqual("evt", evt);
        }
        Assert.Contains("metrics", seen);
    }
}
