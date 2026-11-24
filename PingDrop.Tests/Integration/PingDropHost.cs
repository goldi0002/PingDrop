using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PingDrop.Abstractions;
using PingDrop.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;

namespace PingDrop.Tests.Integration;

/// <summary>Minimal in-memory host running PingDrop on TestServer.</summary>
public sealed class PingDropHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    public HttpClient Client { get; }
    public IPingDropService Service => _app.Services.GetRequiredService<IPingDropService>();
    public List<long> Connected { get; } = new();
    public List<long> Disconnected { get; } = new();

    private PingDropHost(WebApplication app)
    {
        _app = app;
        Client = app.GetTestClient();
        Client.Timeout = TimeSpan.FromSeconds(30);
    }

    public static async Task<PingDropHost> StartAsync(
        Action<PingDropOptions>? configure = null,
        string environment = "Development",
        bool withAuth = false)
    {
        PingDropHost? host = null;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        if (withAuth)
        {
            builder.Services.AddAuthentication(TestAuthHandler.Scheme).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
            builder.Services.AddAuthorization(o => o.AddPolicy("Admin", p => p.RequireClaim("role", "admin")));
        }
        builder.WebHost.UseTestServer();
        builder.Services.AddPingDrop(o =>
        {
            o.TargetKeyResolver = ctx => ctx.Request.Query["uId"];
            o.ClientConnected = id => { lock (host!.Connected) host.Connected.Add(id); return Task.CompletedTask; };
            o.ClientDisconnected = id => { lock (host!.Disconnected) host.Disconnected.Add(id); return Task.CompletedTask; };
            configure?.Invoke(o);
        });
        var app = builder.Build();
        if (withAuth)
        {
            app.UseAuthentication();
            app.UseAuthorization();
        }
        app.MapPingDrop();
        await app.StartAsync();
        host = new PingDropHost(app);
        return host;
    }

    /// <summary>Opens an SSE stream and waits until the server has registered the connection.</summary>
    public async Task<SseStream> ConnectAsync(string? userId = null, string? eventTypes = null)
    {
        var qs = new List<string>();
        if (userId != null) qs.Add($"uId={userId}");
        if (eventTypes != null) qs.Add($"eventType={eventTypes}");
        var url = "/pingdrop/stream" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");

        int before;
        lock (Connected) before = Connected.Count;

        // The server flushes headers as soon as the client is registered, so this returns promptly.
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var responseTask = Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        await responseTask.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => { lock (Connected) return Connected.Count > before; });
        return new SseStream(responseTask, cts);
    }

    public static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not met in time.");
            await Task.Delay(20);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public sealed class SseStream : IAsyncDisposable
{
    private readonly Task<HttpResponseMessage> _responseTask;
    private readonly CancellationTokenSource _cts;
    private StreamReader? _reader;

    public SseStream(Task<HttpResponseMessage> responseTask, CancellationTokenSource cts)
    {
        _responseTask = responseTask;
        _cts = cts;
    }

    /// <summary>Completes once the server has sent headers (immediately after connect).</summary>
    public Task<HttpResponseMessage> GetResponseAsync() => _responseTask;

    /// <summary>Reads the next complete SSE frame (id/event/data).</summary>
    public async Task<(string? Id, string? Event, string? Data)> ReadEventAsync()
    {
        if (_reader is null)
        {
            var response = await _responseTask;
            _reader = new StreamReader(await response.Content.ReadAsStreamAsync(_cts.Token), Encoding.UTF8);
        }

        string? id = null, evt = null, data = null;
        while (true)
        {
            var line = await _reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5), _cts.Token);
            if (line is null) throw new EndOfStreamException();
            if (line.Length == 0)
            {
                if (evt != null || data != null) return (id, evt, data);
                continue;
            }
            if (line.StartsWith("id: ")) id = line[4..];
            else if (line.StartsWith("event: ")) evt = line[7..];
            else if (line.StartsWith("data: ")) data = line[6..];
        }
    }

    /// <summary>Reads the next raw line (including comment lines such as ": ping"), without SSE parsing.</summary>
    public async Task<string> ReadLineAsync()
    {
        if (_reader is null)
        {
            var response = await _responseTask;
            _reader = new StreamReader(await response.Content.ReadAsStreamAsync(_cts.Token), Encoding.UTF8);
        }

        var line = await _reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5), _cts.Token);
        return line ?? throw new EndOfStreamException();
    }

    /// <summary>Reads raw lines until one equals <paramref name="expected"/>; returns all lines read.</summary>
    public async Task<List<string>> ReadUntilLineAsync(string expected)
    {
        var lines = new List<string>();
        while (true)
        {
            var line = await ReadLineAsync();
            lines.Add(line);
            if (line == expected) return lines;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _reader?.Dispose();
        try { (await _responseTask).Dispose(); } catch { /* aborted or never completed */ }
    }
}

/// <summary>Header-driven auth: X-Test-User authenticates; X-Test-Role adds a "role" claim.</summary>
public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-User", out var user))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim> { new(ClaimTypes.Name, user.ToString()) };
        if (Request.Headers.TryGetValue("X-Test-Role", out var role))
            claims.Add(new Claim("role", role.ToString()));

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme)), Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
