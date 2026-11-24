using PingDrop.Abstractions;

namespace PingDropSamples.BackgroundSender;

/// <summary>
/// Publishes from outside any HTTP request: a broadcast "tick" every few seconds
/// and a targeted "private" event to the key "alice" on every third tick.
/// </summary>
public sealed class TickerService(IPingDropService pingDrop, ILogger<TickerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tick = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                tick++;
                try
                {
                    // Everybody receives this.
                    await pingDrop.BroadcastAsync(new { Tick = tick, At = DateTime.UtcNow }, "tick", stoppingToken);

                    // Only connections whose key is "alice" receive this.
                    if (tick % 3 == 0)
                        await pingDrop.SendToAsync("alice", new { Text = $"Hi alice, tick {tick}" }, "private", stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A failed send must not kill the loop.
                    logger.LogWarning(ex, "Ticker send failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }
}
