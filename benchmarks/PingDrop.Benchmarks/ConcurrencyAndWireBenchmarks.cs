using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http;
using PingDrop.Internal;
using PingDrop.Models;

namespace PingDrop.Benchmarks;

/// <summary>Four publishers at once against 100 clients: shows lock contention in the publish path.</summary>
[MemoryDiagnoser]
public class ConcurrentPublishBenchmarks
{
    private PDConnectionStore _store = null!;
    private PingDropHub _hub = null!;
    private PDClientConnection[] _clients = null!;
    private object _payload = null!;

    [Params(512)]
    public int PayloadBytes;

    [GlobalSetup]
    public void Setup()
    {
        _store = new PDConnectionStore();
        var options = new PingDrop.Abstractions.PingDropOptions();
        _hub = new PingDropHub(_store, new PDEventHistoryStore(options.EventHistorySize), new NotificationFactory(), null, options);
        _clients = Enumerable.Range(0, 100).Select(i => _store.Add(null, "user" + i)).ToArray();
        _payload = new { Message = new string('x', PayloadBytes), Id = 1 };
    }

    [Benchmark]
    public async Task FourPublishers_250Each()
    {
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            for (int i = 0; i < 250; i++) await _hub.BroadcastAsync(_payload, "notification");
        })));
        foreach (var c in _clients)
            while (c.NotificationChannel.Reader.TryRead(out _)) { }
    }
}

/// <summary>Writing one SSE frame to one client's response. A broadcast pays this once per client.</summary>
[MemoryDiagnoser]
public class SseFrameBenchmarks
{
    private HttpResponse _response = null!;
    private PDNotification _notification = null!;

    [Params(512)]
    public int PayloadBytes;

    [GlobalSetup]
    public void Setup()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = Stream.Null;
        _response = ctx.Response;
        _notification = new NotificationFactory().CreateNotification(new { Message = new string('x', PayloadBytes) }, "notification");
    }

    [Benchmark]
    public async Task WriteFrame() => await PDSseWriter.WriteAsync(_response, _notification);
}
