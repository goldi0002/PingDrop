using BenchmarkDotNet.Attributes;
using PingDrop.Internal;

namespace PingDrop.Benchmarks;

/// <summary>
/// Cost of one publish: serialize, record in history, resolve recipients and queue to every client.
/// Queues are drained after each publish so the benchmark measures steady state, not queue overflow.
/// </summary>
[MemoryDiagnoser]
public class PublishBenchmarks
{
    private PDConnectionStore _store = null!;
    private PingDropHub _hub = null!;
    private PDClientConnection[] _clients = null!;
    private object _payload = null!;
    private string[] _keys = null!;

    [Params(1, 100, 1000)]
    public int Clients;

    [Params(512)]
    public int PayloadBytes;

    [GlobalSetup]
    public void Setup()
    {
        _store = new PDConnectionStore();
        var options = new PingDrop.Abstractions.PingDropOptions();
        _hub = new PingDropHub(_store, new PDEventHistoryStore(options.EventHistorySize), new NotificationFactory(), null, options);
        _clients = new PDClientConnection[Clients];
        _keys = new string[Clients];
        for (int i = 0; i < Clients; i++)
        {
            _keys[i] = "user" + i;
            _clients[i] = _store.Add(null, _keys[i]);
        }
        _payload = new { Message = new string('x', PayloadBytes), Id = 1, Severity = "info" };
    }

    private void Drain()
    {
        foreach (var c in _clients)
            while (c.NotificationChannel.Reader.TryRead(out _)) { }
    }

    [Benchmark]
    public async Task Broadcast()
    {
        await _hub.BroadcastAsync(_payload, "notification");
        Drain();
    }

    [Benchmark]
    public async Task SendToOneKey()
    {
        await _hub.SendToAsync(_keys[Clients / 2], _payload, "notification");
        Drain();
    }

    [Benchmark]
    public async Task SendToTenKeys()
    {
        var keys = _keys.Take(Math.Min(10, Clients)).ToArray();
        await _hub.SendToAsync(keys, _payload, "notification");
        Drain();
    }
}
