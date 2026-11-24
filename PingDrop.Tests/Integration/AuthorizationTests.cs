using System.Net;
using System.Net.Http.Json;

namespace PingDrop.Tests.Integration;

public class AuthorizationTests
{
    private const string Dash = "/pingdrop/stream/dashboard";

    private static HttpRequestMessage Get(string url, string? user = null, string? role = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (user != null) req.Headers.Add("X-Test-User", user);
        if (role != null) req.Headers.Add("X-Test-Role", role);
        return req;
    }

    [Theory]
    [InlineData("")]
    [InlineData("/app.js")]
    [InlineData("/app.css")]
    [InlineData("/metrics")]
    [InlineData("/about")]
    [InlineData("/connections")]
    [InlineData("/events")]
    public async Task Dashboard_WithPolicy_RejectsAnonymous_AndNonMatchingUser(string route)
    {
        await using var host = await PingDropHost.StartAsync(o => o.Dashboard.AuthorizationPolicy = "Admin", withAuth: true);

        var anon = await host.Client.SendAsync(Get(Dash + route));
        var wrongRole = await host.Client.SendAsync(Get(Dash + route, "bob", "viewer"));

        Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrongRole.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/metrics")]
    [InlineData("/connections")]
    [InlineData("/events")]
    public async Task Dashboard_WithPolicy_AllowsAuthorizedUser(string route)
    {
        await using var host = await PingDropHost.StartAsync(o => o.Dashboard.AuthorizationPolicy = "Admin", withAuth: true);

        var res = await host.Client.SendAsync(Get(Dash + route, "alice", "admin"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Dashboard_WithPolicy_ProtectsSendNotification()
    {
        await using var host = await PingDropHost.StartAsync(o => o.Dashboard.AuthorizationPolicy = "Admin", withAuth: true);
        var body = new { targetType = "broadcast", eventName = "evt", payload = new { a = 1 } };

        var res = await host.Client.PostAsJsonAsync($"{Dash}/send-notification", body);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Dashboard_WithPolicy_ProtectsLiveFeed()
    {
        await using var host = await PingDropHost.StartAsync(o => o.Dashboard.AuthorizationPolicy = "Admin", withAuth: true);

        var res = await host.Client.SendAsync(Get($"{Dash}/sse"));

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Dashboard_NoPolicy_InProduction_IsNotMapped()
    {
        await using var host = await PingDropHost.StartAsync(environment: "Production");

        var res = await host.Client.GetAsync($"{Dash}/metrics");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Dashboard_NoPolicy_InProduction_AllowAnonymous_IsMapped()
    {
        await using var host = await PingDropHost.StartAsync(o => o.Dashboard.AllowAnonymous = true, environment: "Production");

        var res = await host.Client.GetAsync($"{Dash}/metrics");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Dashboard_WithPolicy_InProduction_IsMapped()
    {
        await using var host = await PingDropHost.StartAsync(o => o.Dashboard.AuthorizationPolicy = "Admin", environment: "Production", withAuth: true);

        var res = await host.Client.SendAsync(Get($"{Dash}/metrics", "alice", "admin"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Stream_WithPolicy_RejectsAnonymous_AndWrongRole()
    {
        await using var host = await PingDropHost.StartAsync(o => o.AuthorizationPolicy = "Admin", withAuth: true);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var anon = await host.Client.SendAsync(Get("/pingdrop/stream"), HttpCompletionOption.ResponseHeadersRead, cts.Token);
        var wrong = await host.Client.SendAsync(Get("/pingdrop/stream", "bob", "viewer"), HttpCompletionOption.ResponseHeadersRead, cts.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
    }

    [Fact]
    public async Task Stream_WithPolicy_AllowsAuthorizedClient()
    {
        await using var host = await PingDropHost.StartAsync(o => o.AuthorizationPolicy = "Admin", withAuth: true);
        using var req = Get("/pingdrop/stream", "alice", "admin");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));

        var pending = host.Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        using var res = await pending;

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Stream_WithoutPolicy_StaysAnonymous()
    {
        await using var host = await PingDropHost.StartAsync(withAuth: true);
        await using var sse = await host.ConnectAsync("alice");

        await host.Service.SendToAsync("alice", "x", "evt");

        Assert.Equal("evt", (await sse.ReadEventAsync()).Event);
    }
}
