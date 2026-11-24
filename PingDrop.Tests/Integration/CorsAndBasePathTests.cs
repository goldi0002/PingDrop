using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using PingDrop.Abstractions;

namespace PingDrop.Tests.Integration;

public class CorsAndBasePathTests
{
    private const string Allowed = "https://app.example.com";
    private const string Other = "https://evil.example.net";
    private const string Stream = "/pingdrop/stream";
    private const string Dash = "/pingdrop/stream/dashboard";

    private static void UseCorsPipeline(WebApplication app)
    {
        app.UseRouting();
        app.UseCors();
    }

    private static Task<PingDropHost> StartWithOriginsAsync() =>
        PingDropHost.StartAsync(o => o.AllowedOrigins = [Allowed], configureApp: UseCorsPipeline);

    /// <summary>Sends a request and returns once headers arrive; the (possibly endless) body is abandoned.</summary>
    private static async Task<HttpResponseMessage> SendAsync(PingDropHost host, HttpMethod method, string url, params (string Name, string Value)[] headers)
    {
        var request = new HttpRequestMessage(method, url);
        foreach (var (name, value) in headers) request.Headers.TryAddWithoutValidation(name, value);
        return await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static string? Header(HttpResponseMessage res, string name) =>
        res.Headers.TryGetValues(name, out var v) ? string.Join(",", v) : null;

    [Theory]
    [InlineData(Stream)]
    [InlineData(Stream + "/client.js")]
    public async Task AllowedOrigin_GetsCorsHeaders(string url)
    {
        await using var host = await StartWithOriginsAsync();

        using var res = await SendAsync(host, HttpMethod.Get, url, ("Origin", Allowed));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(Allowed, Header(res, "Access-Control-Allow-Origin"));
        Assert.Equal("true", Header(res, "Access-Control-Allow-Credentials"));
    }

    [Theory]
    [InlineData(Stream)]
    [InlineData(Stream + "/client.js")]
    public async Task DisallowedOrigin_GetsNoCorsHeaders(string url)
    {
        await using var host = await StartWithOriginsAsync();

        using var res = await SendAsync(host, HttpMethod.Get, url, ("Origin", Other));

        Assert.Null(Header(res, "Access-Control-Allow-Origin"));
        Assert.Null(Header(res, "Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task RequestWithoutOrigin_GetsNoCorsHeaders()
    {
        await using var host = await StartWithOriginsAsync();

        using var res = await SendAsync(host, HttpMethod.Get, Stream + "/client.js");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Null(Header(res, "Access-Control-Allow-Origin"));
    }

    [Theory]
    [InlineData(Stream)]
    [InlineData(Stream + "/client.js")]
    public async Task Preflight_WithLastEventIdHeader_SucceedsForAllowedOrigin(string url)
    {
        await using var host = await StartWithOriginsAsync();

        using var res = await SendAsync(host, HttpMethod.Options, url,
            ("Origin", Allowed), ("Access-Control-Request-Method", "GET"), ("Access-Control-Request-Headers", "last-event-id"));

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        Assert.Equal(Allowed, Header(res, "Access-Control-Allow-Origin"));
        Assert.Contains("Last-Event-ID", Header(res, "Access-Control-Allow-Headers"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("true", Header(res, "Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task Preflight_ForDisallowedOrigin_IsNotApproved()
    {
        await using var host = await StartWithOriginsAsync();

        using var res = await SendAsync(host, HttpMethod.Options, Stream,
            ("Origin", Other), ("Access-Control-Request-Method", "GET"), ("Access-Control-Request-Headers", "last-event-id"));

        Assert.Null(Header(res, "Access-Control-Allow-Origin"));
        Assert.Null(Header(res, "Access-Control-Allow-Headers"));
    }

    [Fact]
    public async Task Preflight_ForUnlistedHeader_IsNotApproved()
    {
        await using var host = await StartWithOriginsAsync();

        using var res = await SendAsync(host, HttpMethod.Options, Stream,
            ("Origin", Allowed), ("Access-Control-Request-Method", "GET"), ("Access-Control-Request-Headers", "x-custom"));

        Assert.DoesNotContain("x-custom", Header(res, "Access-Control-Allow-Headers") ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NamedPolicy_IsAppliedToStreamAndClient()
    {
        await using var host = await PingDropHost.StartAsync(
            o => o.CorsPolicy = "Custom",
            configureServices: s => s.AddCors(c => c.AddPolicy("Custom", p => p.WithOrigins(Allowed).WithHeaders("Last-Event-ID"))),
            configureApp: UseCorsPipeline);

        using var stream = await SendAsync(host, HttpMethod.Get, Stream, ("Origin", Allowed));
        using var client = await SendAsync(host, HttpMethod.Get, Stream + "/client.js", ("Origin", Allowed));
        using var denied = await SendAsync(host, HttpMethod.Get, Stream + "/client.js", ("Origin", Other));

        Assert.Equal(Allowed, Header(stream, "Access-Control-Allow-Origin"));
        Assert.Equal(Allowed, Header(client, "Access-Control-Allow-Origin"));
        Assert.Null(Header(denied, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Dashboard_NeverGetsCorsHeaders_EvenWithPermissiveDefaultPolicy()
    {
        await using var host = await PingDropHost.StartAsync(
            o => o.AllowedOrigins = [Allowed],
            configureServices: s => s.AddCors(c => c.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod())),
            configureApp: UseCorsPipeline);

        foreach (var path in new[] { Dash, Dash + "/metrics", Dash + "/app.js", Dash + "/sse" })
        {
            using var res = await SendAsync(host, HttpMethod.Get, path, ("Origin", Allowed));
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Null(Header(res, "Access-Control-Allow-Origin"));
        }

        using var preflight = await SendAsync(host, HttpMethod.Options, Dash + "/send-notification",
            ("Origin", Allowed), ("Access-Control-Request-Method", "POST"), ("Access-Control-Request-Headers", "content-type"));
        Assert.Null(Header(preflight, "Access-Control-Allow-Origin"));

        using var post = await host.Client.PostAsJsonAsync(Dash + "/send-notification", new { targetType = "broadcast", eventName = "e", payload = new { } });
        Assert.Null(Header(post, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task WithoutCorsOptions_NoCorsHeadersAreAdded()
    {
        await using var host = await PingDropHost.StartAsync();

        using var res = await SendAsync(host, HttpMethod.Get, Stream + "/client.js", ("Origin", Allowed));

        Assert.Null(Header(res, "Access-Control-Allow-Origin"));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://*.example.com")]
    [InlineData("")]
    public async Task AllowedOrigins_RejectsWildcardsAndBlanks(string origin)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => PingDropHost.StartAsync(o => o.AllowedOrigins = [origin]));
    }

    [Fact]
    public async Task AllowedOrigins_CannotBeCombinedWithCorsPolicy()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PingDropHost.StartAsync(o => { o.AllowedOrigins = [Allowed]; o.CorsPolicy = "x"; }));
    }

    // ---- dashboard base path ----

    private static void UsePathBase(WebApplication app, string pathBase)
    {
        app.UsePathBase(pathBase);
        app.UseRouting();
    }

    [Fact]
    public async Task Dashboard_UnderPathBase_PrefixesUrlsAndRoutesResolve()
    {
        await using var host = await PingDropHost.StartAsync(configureApp: app => UsePathBase(app, "/app"));

        var html = await host.Client.GetStringAsync("/app" + Dash);

        Assert.Contains("window.pingdrop.baseUrl = '/app" + Dash + "';", html);
        Assert.Contains("href=\"/app" + Dash + "/app.css\"", html);
        Assert.Contains("src=\"/app" + Dash + "/app.js?v=pingdrop\"", html);
        Assert.DoesNotContain("{{", html);

        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/app" + Dash + "/metrics")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/app" + Dash + "/app.js")).StatusCode);
        using var sse = await SendAsync(host, HttpMethod.Get, "/app" + Dash + "/sse");
        Assert.Equal("text/event-stream", sse.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Dashboard_WithoutPathBase_UsesUnprefixedUrls()
    {
        await using var host = await PingDropHost.StartAsync();

        var html = await host.Client.GetStringAsync(Dash);

        Assert.Contains("window.pingdrop.baseUrl = '" + Dash + "';", html);
        Assert.Contains("href=\"" + Dash + "/app.css\"", html);
    }

    [Fact]
    public async Task Dashboard_PathBaseWithQuoteCharacters_CannotBreakOutOfTheScript()
    {
        await using var host = await PingDropHost.StartAsync(configureApp: app => UsePathBase(app, "/a'b"));

        var html = await host.Client.GetStringAsync("/a%27b" + Dash);

        Assert.DoesNotContain("/a'b", html);
        Assert.Contains("/a%27b" + Dash, html);
    }

    [Fact]
    public async Task PathOverride_MovesDashboard_AndOldDefaultReturns404()
    {
        await using var host = await PingDropHost.StartAsync(o => o.Dashboard.PathOverride = "admin/pingdrop/");

        var html = await host.Client.GetStringAsync("/admin/pingdrop");
        Assert.Contains("window.pingdrop.baseUrl = '/admin/pingdrop';", html);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/admin/pingdrop/metrics")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/admin/pingdrop/app.css")).StatusCode);
        using var sse = await SendAsync(host, HttpMethod.Get, "/admin/pingdrop/sse");
        Assert.Equal(HttpStatusCode.OK, sse.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync(Dash)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync(Dash + "/metrics")).StatusCode);
        // The SSE endpoint itself is unaffected.
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync(Stream + "/client.js")).StatusCode);
    }

    [Fact]
    public async Task PathOverride_CombinesWithPathBase()
    {
        await using var host = await PingDropHost.StartAsync(o => o.Dashboard.PathOverride = "/admin", configureApp: app => UsePathBase(app, "/app"));

        var html = await host.Client.GetStringAsync("/app/admin");

        Assert.Contains("window.pingdrop.baseUrl = '/app/admin';", html);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/app/admin/metrics")).StatusCode);
    }

    [Theory]
    [InlineData(null, "/pingdrop/stream/dashboard")]
    [InlineData("", "/pingdrop/stream/dashboard")]
    [InlineData("/", "/pingdrop/stream/dashboard")]
    [InlineData("/admin/pingdrop", "/admin/pingdrop")]
    [InlineData("admin/pingdrop//", "/admin/pingdrop")]
    public void Path_ReturnsEffectivePath(string? overridePath, string expected)
    {
        var options = new PingDropDashboardOptions { PathOverride = overridePath };

        Assert.Equal(expected, options.Path);
    }

    // ---- offline ----

    [Theory]
    [InlineData("")]
    [InlineData("/app.css")]
    [InlineData("/app.js")]
    public async Task DashboardAssets_HaveNoExternalReferences(string suffix)
    {
        await using var host = await PingDropHost.StartAsync();

        var body = await host.Client.GetStringAsync(Dash + suffix);

        // The only allowed URL-like text is the inline SVG XML namespace.
        var urls = Regex.Matches(body, @"(https?:)?//[A-Za-z0-9.-]+\.[a-z]{2,}[^\s""')]*")
            .Select(m => m.Value).Where(v => !v.StartsWith("http://www.w3.org/2000/svg")).ToList();
        Assert.Empty(urls);
    }
}
