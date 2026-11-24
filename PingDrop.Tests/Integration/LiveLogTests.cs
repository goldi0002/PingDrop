using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PingDrop.Abstractions;
using PingDrop.Internal;

namespace PingDrop.Tests.Integration;

public class LiveLogTests
{
    private const string Base = "/pingdrop/stream/dashboard";

    /// <summary>Forwards to the real publisher and records every published event type.</summary>
    private sealed class SpyPublisher(PDEventPublisher inner) : IEventPublisher
    {
        private readonly List<string> _published = new();
        public bool IsActive => inner.IsActive;
        public ValueTask PublishAsync<T>(T notification, string eventType = "message", CancellationToken cancellationToken = default)
        {
            lock (_published) _published.Add(eventType);
            return inner.PublishAsync(notification, eventType, cancellationToken);
        }
        public int Count() { lock (_published) return _published.Count; }
    }

    private static void UseSpy(IServiceCollection services)
    {
        services.AddSingleton<PDEventPublisher>();
        services.AddSingleton<SpyPublisher>(sp => new SpyPublisher(sp.GetRequiredService<PDEventPublisher>()));
        services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<SpyPublisher>());
    }

    private static async Task<SseStream> ConnectDashboardAsync(PingDropHost host)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var task = host.Client.GetAsync($"{Base}/sse", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        await task.WaitAsync(TimeSpan.FromSeconds(15));
        return new SseStream(task, cts);
    }

    private static async Task<List<string>> ReadLogMessagesAsync(SseStream dash, int count)
    {
        var msgs = new List<string>();
        while (msgs.Count < count)
        {
            try
            {
                var (_, evt, data) = await dash.ReadEventAsync();
                if (evt == "log") msgs.Add(JsonDocument.Parse(data!).RootElement.GetProperty("msg").GetString()!);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"Expected {count} log lines but only received {msgs.Count}: [{string.Join(" | ", msgs)}]");
            }
        }
        return msgs;
    }

    private static async Task RoundTripAsync(PingDropHost host)
    {
        var alice = await host.ConnectAsync("alice");
        await host.Service.BroadcastAsync("{}", "evt");
        Assert.Equal("evt", (await alice.ReadEventAsync()).Event);
        await alice.DisposeAsync();
        await PingDropHost.WaitUntilAsync(() => { lock (host.Disconnected) return host.Disconnected.Count == 1; });
    }

    [Fact]
    public async Task DashboardDisabled_PublishesNothing()
    {
        await using var host = await PingDropHost.StartAsync(o => o.EnableDashboard = false, configureServices: UseSpy);

        await RoundTripAsync(host);

        Assert.Equal(0, host.Services.GetRequiredService<SpyPublisher>().Count());
    }

    [Fact]
    public async Task NoDashboardFeedConnected_PublishesNothing()
    {
        await using var host = await PingDropHost.StartAsync(o => o.Dashboard.LogEachDispatch = true, configureServices: UseSpy);

        await RoundTripAsync(host);
        var res = await host.Client.PostAsJsonAsync($"{Base}/send-notification",
            new { TargetType = "broadcast", EventName = "evt", Payload = "{}" });
        Assert.True(res.IsSuccessStatusCode);

        Assert.Equal(0, host.Services.GetRequiredService<SpyPublisher>().Count());
    }

    [Fact]
    public async Task DashboardFeed_ReceivesConnectAndDisconnect_ButNotDispatch_ByDefault()
    {
        await using var host = await PingDropHost.StartAsync();
        await using var dash = await ConnectDashboardAsync(host);

        await RoundTripAsync(host);

        var msgs = await ReadLogMessagesAsync(dash, 2);
        Assert.StartsWith("Client connected", msgs[0]);
        Assert.StartsWith("Client disconnected", msgs[1]);
    }

    [Fact]
    public async Task DashboardFeed_ReceivesDispatchLines_WhenLogEachDispatch()
    {
        await using var host = await PingDropHost.StartAsync(o => o.Dashboard.LogEachDispatch = true);
        await using var dash = await ConnectDashboardAsync(host);

        await RoundTripAsync(host);

        var msgs = await ReadLogMessagesAsync(dash, 3);
        Assert.StartsWith("Client connected", msgs[0]);
        Assert.StartsWith("Event dispatched", msgs[1]);
        Assert.StartsWith("Client disconnected", msgs[2]);
    }
}
