using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PingDrop.Abstractions;

namespace PingDrop.Tests.Integration;

public class ShutdownTests
{
    private static async Task<SseStream> ConnectDashboardAsync(PingDropHost host)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var task = host.Client.GetAsync("/pingdrop/stream/dashboard/sse", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        await task.WaitAsync(TimeSpan.FromSeconds(5));
        return new SseStream(task, cts);
    }

    private static async Task AssertEndsAsync(SseStream sse)
    {
        // Drain whatever is buffered (the retry/connected frame); the stream must then hit end-of-stream.
        await Assert.ThrowsAsync<EndOfStreamException>(async () =>
        {
            while (true) await sse.ReadLineAsync();
        });
    }

    [Fact]
    public async Task StopAsync_EndsAllStreams_QuicklyAndRunsDisconnectCallbacks()
    {
        var logs = new LogCollector();
        await using var host = await PingDropHost.StartAsync(
            o => o.HeartbeatInterval = TimeSpan.FromSeconds(30),
            configureLogging: b => b.AddProvider(logs));
        await using var a = await host.ConnectAsync("alice");
        await using var b = await host.ConnectAsync("bob");
        await using var dash = await ConnectDashboardAsync(host);
        var store = host.Services.GetRequiredService<IPDConnectionStore>();
        await PingDropHost.WaitUntilAsync(() => store.GetAllIncludingDashboard().Count() == 3);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
        await AssertEndsAsync(a);
        await AssertEndsAsync(b);
        await AssertEndsAsync(dash);

        await PingDropHost.WaitUntilAsync(() => { lock (host.Disconnected) return host.Disconnected.Count == 2; });
        await PingDropHost.WaitUntilAsync(() => !store.GetAllIncludingDashboard().Any());
        Assert.DoesNotContain(logs.Entries, e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task ConnectionAfterStopping_IsRejectedWith503()
    {
        await using var host = await PingDropHost.StartAsync();
        await host.StopAsync();

        using var res = await host.Client.GetAsync("/pingdrop/stream", HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, res.StatusCode);
    }
}
