# Performance

These are the measured costs of the publish and delivery paths, and how to reproduce them. The numbers are from BenchmarkDotNet's `--job short` (3 iterations, so the error margins are wide) on a 4-core sandbox VM (Intel Xeon 2.3 GHz, .NET 8.0.34, Linux). Treat them as relative before/after comparisons, not as absolute capacity figures; run the benchmarks on your own hardware before you size anything.

## What is measured

The benchmark project is `benchmarks/PingDrop.Benchmarks` (not part of `PingDrop.sln`; the library exposes its internals to it). All runs use a 512-byte payload.

- `PublishBenchmarks`: one `BroadcastAsync`, `SendToAsync(key)` or `SendToAsync(keys)` (10 keys) against 1, 100 and 1000 connected clients: serialize, record in the history, resolve the recipients and queue to each client. The clients' queues are drained after every call, which adds a constant of about 15 us per 1000 clients to every row.
- `SseFrameBenchmarks`: writing one event to one client's response (a broadcast pays this once per client), and the first write of an event, which encodes the frame.
- `ConcurrentPublishBenchmarks`: four threads publishing 250 broadcasts each to 100 clients.
- `HeartbeatBenchmarks`: one wake-up of a connection's wait loop.

```
cd benchmarks/PingDrop.Benchmarks
dotnet build -c Release
dotnet run -c Release --no-build -- --filter '*' --job short --exporters github
```

The full run takes about 5 minutes. Do not run the tests at the same time.

## Before and after

Both columns were measured in the same sandbox, one run each.

| Benchmark | Before | After |
|---|---|---|
| Broadcast, 1 client | 858 ns, 1.49 KB | 673 ns, 1.17 KB |
| Broadcast, 100 clients | 11,481 ns, 4.32 KB | 9,181 ns, 1.17 KB |
| Broadcast, 1000 clients | 108,960 ns, 25.42 KB | 91,022 ns, 1.17 KB |
| SendTo one key, 1 / 100 / 1000 clients | 959 / 2,751 / 18,314 ns, 1.47 KB | 688 / 2,402 / 18,819 ns, 1.23 KB |
| SendTo ten keys, 1 / 100 / 1000 clients | 1,115 / 6,526 / 23,183 ns, 2.27 / 4.53 / 4.53 KB | 769 / 3,967 / 20,346 ns, 1.63 / 2.69 / 2.69 KB |
| Write one frame to one client | 376 ns, 1.17 KB | 94 ns, 0 B |
| First write of an event (encodes the frame) | not measured separately | 264 ns, 656 B |
| Four publishers, 250 broadcasts each, 100 clients | 17.4 ms, 4.18 MB | 14.7 ms, 1.11 MB |
| Connection wake-up (heartbeat wait) | 182 ns, 144 B | 145 ns, 0 B |

What changed, and what did not:

- A broadcast no longer encodes its SSE frame once per client. The frame is built on first use, cached on the notification and the same bytes are copied to every client and to replay. Per client and event this is the largest saving (about 280 ns and 1.17 KB).
- Publishing no longer copies the connection dictionary, runs LINQ or builds a list. The connection store keeps immutable array snapshots, rebuilt on connect and disconnect, which publishing reads without a lock and without allocating. The allocation per broadcast is now flat in the number of clients.
- The payload is serialized before the publish lock is taken. The lock still covers id assignment, the history entry, the recipient lookup and the delivery, because replay depends on that order.
- Concurrent publishers still do not scale much: the delivery to every client's queue happens under the lock, and that is the bulk of the work for 100 clients. Re-measured runs showed no significant change in wall-clock time (within noise); only allocations dropped (about 4.2 MB to 1.1 MB per run). Moving delivery out of the lock would need a different replay-ordering guarantee.
- Single-key sends to 1000 clients are dominated by draining the 1000 queues in the benchmark itself, so they show no change.
- Writing each queued event and flushing once per batch was considered and not done: it would change when delivery counters and the live log are updated relative to the flush, and the benchmark harness does not exercise a real socket, so there was no way to show a benefit.

Connecting and disconnecting a client copies the snapshot arrays, which is O(connected clients) per connect. That is cheap next to a send (8 KB at 1000 clients), but a burst of tens of thousands of simultaneous connects pays quadratic copying.
