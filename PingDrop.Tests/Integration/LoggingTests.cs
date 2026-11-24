using Microsoft.Extensions.Logging;
using PingDrop.Internal;

namespace PingDrop.Tests.Integration;

public class LoggingTests
{
    [Fact]
    public async Task ConnectAndDisconnect_AreLoggedAtDebug()
    {
        var logs = new LogCollector();
        await using var host = await PingDropHost.StartAsync(configureLogging: b => b.SetMinimumLevel(LogLevel.Debug).AddProvider(logs));

        var sse = await host.ConnectAsync();
        await PingDropHost.WaitUntilAsync(() => logs.Entries.Any(e => e.Message.Contains("connected (subscriber")));
        await sse.DisposeAsync();
        await PingDropHost.WaitUntilAsync(() => logs.Entries.Any(e => e.Message.EndsWith("disconnected.")));

        var clientLogs = logs.Entries.Where(e => e.Category == "PingDrop" && e.Message.StartsWith("Client ")).ToList();
        Assert.Equal(2, clientLogs.Count);
        Assert.All(clientLogs, e => Assert.Equal(LogLevel.Debug, e.Level));
    }

    [Fact]
    public async Task ThrowingClientConnectedCallback_IsLoggedAsError_AndConnectionStaysOpen()
    {
        var logs = new LogCollector();
        var boom = new InvalidOperationException("boom");
        await using var host = await PingDropHost.StartAsync(
            o => o.ClientConnected = _ => throw boom,
            configureLogging: b => b.AddProvider(logs));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var response = await host.Client.GetAsync("/pingdrop/stream", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        await PingDropHost.WaitUntilAsync(() => ((PingDropHub)host.Service).ConnectedClientsCount == 1);
        await host.Service.BroadcastAsync("hi", "evt");

        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cts.Token));
        var lines = new List<string>();
        while (!lines.Contains("event: evt")) lines.Add((await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5), cts.Token))!);

        var error = Assert.Single(logs.Entries, e => e.Level == LogLevel.Error);
        Assert.Equal("PingDrop", error.Category);
        Assert.Same(boom, error.Exception);
    }

    [Fact]
    public async Task ThrowingClientDisconnectedCallback_IsLoggedAsError()
    {
        var logs = new LogCollector();
        await using var host = await PingDropHost.StartAsync(
            o => o.ClientDisconnected = _ => throw new InvalidOperationException("bye"),
            configureLogging: b => b.AddProvider(logs));

        var sse = await host.ConnectAsync();
        await sse.DisposeAsync();

        await PingDropHost.WaitUntilAsync(() => logs.Entries.Any(e => e.Level == LogLevel.Error));
        Assert.Equal("bye", logs.Entries.First(e => e.Level == LogLevel.Error).Exception?.Message);
    }
}
