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
}
