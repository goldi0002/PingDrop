using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http;
using PingDrop.Internal;
using PingDrop.Models;
using System.Threading.Channels;

namespace PingDrop.Benchmarks;

/// <summary>One wake-up of a connection's wait loop (an event is already queued, so no heartbeat is written).</summary>
[MemoryDiagnoser]
public class HeartbeatBenchmarks
{
    private HttpResponse _response = null!;
    private PDClientConnection _client = null!;
    private PDHeartbeatWaiter _waiter = null!;
    private readonly PDNotification _notification = new() { Id = 1, EventType = "e", Payload = "{}" };
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    [GlobalSetup]
    public void Setup()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = Stream.Null;
        _response = ctx.Response;
        _client = new PDClientConnection();
        _waiter = new PDHeartbeatWaiter(_client.NotificationChannel.Reader, _response, Interval, _client, CancellationToken.None);
    }

    [Benchmark(Baseline = true)]
    public async Task<bool> NewSourceEachWait()
    {
        _client.TryWriteNotification(_notification);
        bool result = await WaitPerCall(_client.NotificationChannel.Reader, _response, Interval, _client, CancellationToken.None);
        _client.NotificationChannel.Reader.TryRead(out _);
        return result;
    }

    [Benchmark]
    public async Task<bool> ReusedSource()
    {
        _client.TryWriteNotification(_notification);
        bool result = await _waiter.WaitToReadAsync();
        _client.NotificationChannel.Reader.TryRead(out _);
        return result;
    }

    // The previous implementation, kept here as the baseline for comparison.
    private static async ValueTask<bool> WaitPerCall(ChannelReader<PDNotification> reader, HttpResponse response,
        TimeSpan interval, PDClientConnection client, CancellationToken cancellationToken)
    {
        while (true)
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(interval);
            try
            {
                return await reader.WaitToReadAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await PDSseWriter.WritePingAsync(response, cancellationToken);
                client.MarkActivity();
            }
        }
    }
}
