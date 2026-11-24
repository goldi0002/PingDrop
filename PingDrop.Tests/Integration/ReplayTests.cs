using Microsoft.Extensions.Logging;
using PingDrop.Internal;

namespace PingDrop.Tests.Integration;

public class ReplayTests
{
    private static async Task<List<long>> ReadIdsAsync(SseStream sse, int count)
    {
        var ids = new List<long>();
        for (int i = 0; i < count; i++) ids.Add(long.Parse((await sse.ReadEventAsync()).Id!));
        return ids;
    }

    /// <summary>Asserts that nothing further arrives within a short window.</summary>
    private static async Task AssertNothingMoreAsync(SseStream sse)
    {
        var read = sse.ReadEventAsync();
        var finished = await Task.WhenAny(read, Task.Delay(400));
        Assert.NotSame(read, finished);
    }

    [Fact]
    public async Task MissedBroadcasts_AreReplayed_InOrder_BeforeLiveEvents()
    {
        await using var host = await PingDropHost.StartAsync();
        await using (var first = await host.ConnectAsync())
        {
            await host.Service.BroadcastAsync("one", "e");
            Assert.Equal("1", (await first.ReadEventAsync()).Id);
        }
        await PingDropHost.WaitUntilAsync(() => host.Disconnected.Count == 1);

        // Sent while nobody is connected.
        await host.Service.BroadcastAsync("two", "e");
        await host.Service.BroadcastAsync("three", "e");

        await using var again = await host.ConnectAsync(lastEventIdHeader: "1");
        await host.Service.BroadcastAsync("four", "e");

        Assert.Equal(new long[] { 2, 3, 4 }, await ReadIdsAsync(again, 3));
        await AssertNothingMoreAsync(again);
    }

    [Fact]
    public async Task QueryParameter_Works_AsFallback()
    {
        await using var host = await PingDropHost.StartAsync();
        await using (var first = await host.ConnectAsync()) { }
        await PingDropHost.WaitUntilAsync(() => host.Disconnected.Count == 1);
        await host.Service.BroadcastAsync("a", "e");
        await host.Service.BroadcastAsync("b", "e");

        await using var again = await host.ConnectAsync(lastEventIdQuery: "1");

        Assert.Equal(new long[] { 2 }, await ReadIdsAsync(again, 1));
    }

    [Fact]
    public async Task Header_TakesPrecedenceOverQuery()
    {
        await using var host = await PingDropHost.StartAsync();
        await using var keep = await host.ConnectAsync();
        for (int i = 0; i < 4; i++) await host.Service.BroadcastAsync(i, "e");

        await using var again = await host.ConnectAsync(lastEventIdHeader: "3", lastEventIdQuery: "0");

        Assert.Equal(new long[] { 4 }, await ReadIdsAsync(again, 1));
    }

    [Fact]
    public async Task Targeted_Events_AreReplayedOnlyToTheRightKey()
    {
        await using var host = await PingDropHost.StartAsync();
        await using var keepAlice = await host.ConnectAsync("alice");
        await using var keepBob = await host.ConnectAsync("bob");
        await host.Service.BroadcastAsync("b1", "e");                                // 1
        await host.Service.SendToAsync("alice", "for-alice", "e");                   // 2
        await host.Service.SendToAsync("bob", "for-bob", "e");                       // 3
        await host.Service.SendToAsync(new[] { "alice", "carol" }, "multi-ac", "e"); // 4
        await host.Service.SendToAsync(new[] { "bob", "carol" }, "multi-bc", "e");   // 5
        await host.Service.BroadcastAsync("b2", "e");                                // 6

        await using var alice = await host.ConnectAsync("alice", lastEventIdHeader: "0");
        await using var carol = await host.ConnectAsync("carol", lastEventIdHeader: "0");
        await using var anon = await host.ConnectAsync(lastEventIdHeader: "0");
        await using var ali = await host.ConnectAsync("ali", lastEventIdHeader: "0"); // prefix of a key, must not match

        Assert.Equal(new long[] { 1, 2, 4, 6 }, await ReadIdsAsync(alice, 4));
        Assert.Equal(new long[] { 1, 4, 5, 6 }, await ReadIdsAsync(carol, 4));
        Assert.Equal(new long[] { 1, 6 }, await ReadIdsAsync(anon, 2));
        Assert.Equal(new long[] { 1, 6 }, await ReadIdsAsync(ali, 2));
        await AssertNothingMoreAsync(alice);
    }

    [Fact]
    public async Task KeyContainingComma_IsNotConfusedWithMultiKeySend()
    {
        await using var host = await PingDropHost.StartAsync();
        await using var keep = await host.ConnectAsync("a");
        await using var keep2 = await host.ConnectAsync("b");
        await host.Service.SendToAsync(new[] { "a", "b" }, "x", "e"); // display TargetKey is "a,b"

        await using var weird = await host.ConnectAsync("a,b", lastEventIdHeader: "0");

        await AssertNothingMoreAsync(weird);
    }

    [Fact]
    public async Task EventTypeFilter_IsRespected()
    {
        await using var host = await PingDropHost.StartAsync();
        await using var keep = await host.ConnectAsync();
        await host.Service.BroadcastAsync("1", "alpha");
        await host.Service.BroadcastAsync("2", "beta");
        await host.Service.BroadcastAsync("3", "alpha");

        await using var again = await host.ConnectAsync(eventTypes: "beta", lastEventIdHeader: "0");

        var (id, evt, _) = await again.ReadEventAsync();
        Assert.Equal(("2", "beta"), (id, evt));
        await AssertNothingMoreAsync(again);
    }

    [Fact]
    public async Task LastEventId_BeyondNewest_ReplaysNothing()
    {
        await using var host = await PingDropHost.StartAsync();
        await using var keep = await host.ConnectAsync();
        await host.Service.BroadcastAsync("1", "e");
        await host.Service.BroadcastAsync("2", "e");

        await using var restarted = await host.ConnectAsync(lastEventIdHeader: "500");
        await AssertNothingMoreAsync(restarted);

        // Live events still arrive (and are not suppressed by the stale id).
        await host.Service.BroadcastAsync("3", "e");
        Assert.Equal(new long[] { 3 }, await ReadIdsAsync(restarted, 1));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("99999999999999999999999")]
    public async Task InvalidLastEventId_IsIgnored(string value)
    {
        await using var host = await PingDropHost.StartAsync();
        await using var keep = await host.ConnectAsync();
        await host.Service.BroadcastAsync("1", "e");

        await using var again = await host.ConnectAsync(lastEventIdHeader: value);
        await AssertNothingMoreAsync(again);

        await host.Service.BroadcastAsync("2", "e");
        Assert.Equal(new long[] { 2 }, await ReadIdsAsync(again, 1));
    }

    [Fact]
    public async Task EnableReplayFalse_ReplaysNothing()
    {
        await using var host = await PingDropHost.StartAsync(o => o.EnableReplay = false);
        await using var keep = await host.ConnectAsync();
        await host.Service.BroadcastAsync("1", "e");

        await using var again = await host.ConnectAsync(lastEventIdHeader: "0");
        await AssertNothingMoreAsync(again);
        await using var viaQuery = await host.ConnectAsync(lastEventIdQuery: "0");
        await AssertNothingMoreAsync(viaQuery);
    }

    [Fact]
    public async Task EnableReplayFalse_DoesNotRecordEventsWithoutClients()
    {
        await using var host = await PingDropHost.StartAsync(o => o.EnableReplay = false);
        await host.Service.BroadcastAsync("nobody", "e");

        await using var sse = await host.ConnectAsync();
        await host.Service.BroadcastAsync("somebody", "e");

        // No id was consumed by the undelivered event.
        Assert.Equal(new long[] { 1 }, await ReadIdsAsync(sse, 1));
    }

    [Fact]
    public async Task GapLargerThanHistory_ReplaysWhatIsRetained()
    {
        await using var host = await PingDropHost.StartAsync(o => o.EventHistorySize = 3);
        await using var keep = await host.ConnectAsync();
        for (int i = 1; i <= 10; i++) await host.Service.BroadcastAsync(i, "e");

        await using var again = await host.ConnectAsync(lastEventIdHeader: "2");

        Assert.Equal(new long[] { 8, 9, 10 }, await ReadIdsAsync(again, 3));
        await AssertNothingMoreAsync(again);
    }

    [Fact]
    public async Task Replay_LargerThanQueue_IsNotLost()
    {
        await using var host = await PingDropHost.StartAsync(o => { o.ClientQueueCapacity = 2; o.EventHistorySize = 50; });
        await using var keep = await host.ConnectAsync();
        for (int i = 1; i <= 30; i++) await host.Service.BroadcastAsync(i, "e");

        await using var again = await host.ConnectAsync(lastEventIdHeader: "0");

        Assert.Equal(Enumerable.Range(1, 30).Select(i => (long)i), await ReadIdsAsync(again, 30));
    }

    [Fact]
    public async Task Replay_CountsAsDeliveries_ButNotAsPublished()
    {
        await using var host = await PingDropHost.StartAsync();
        var hub = (PingDropHub)host.Service;
        await using var keep = await host.ConnectAsync();
        await host.Service.BroadcastAsync("1", "e");
        long attempted = hub.AttemptedDeliveries;

        await using var again = await host.ConnectAsync(lastEventIdHeader: "0");
        await ReadIdsAsync(again, 1);

        Assert.Equal(1, hub.TotalEventsPublished);
        Assert.Equal(attempted + 1, hub.AttemptedDeliveries);
    }

    [Fact]
    public async Task DashboardFeed_GetsNoReplay()
    {
        await using var host = await PingDropHost.StartAsync();
        await using var keep = await host.ConnectAsync();
        await host.Service.BroadcastAsync("1", "e");

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var request = new HttpRequestMessage(HttpMethod.Get, "/pingdrop/stream/dashboard/sse");
        request.Headers.TryAddWithoutValidation("Last-Event-ID", "0");
        var task = host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        await task.WaitAsync(TimeSpan.FromSeconds(5));
        await using var dash = new SseStream(task, cts);

        await AssertNothingMoreAsync(dash);
    }

    [Fact]
    public async Task Replay_IsLoggedAtDebug()
    {
        var logs = new LogCollector();
        await using var host = await PingDropHost.StartAsync(configureLogging: b => b.SetMinimumLevel(LogLevel.Debug).AddProvider(logs));
        await using var keep = await host.ConnectAsync();
        await host.Service.BroadcastAsync("1", "e");
        await host.Service.BroadcastAsync("2", "e");

        await using var again = await host.ConnectAsync(lastEventIdHeader: "0");
        await ReadIdsAsync(again, 2);

        await PingDropHost.WaitUntilAsync(() => logs.Entries.Any(e => e.Message.StartsWith("Replayed 2 missed event(s)")));
        var entry = Assert.Single(logs.Entries, e => e.Message.StartsWith("Replayed 2 missed event(s)"));
        Assert.Equal(LogLevel.Debug, entry.Level);
    }

    [Fact]
    public async Task ConcurrentPublishing_DuringConnect_HasNoDuplicatesOrGaps()
    {
        for (int round = 0; round < 10; round++)
        {
            await using var host = await PingDropHost.StartAsync(o => { o.EventHistorySize = 100_000; o.ClientQueueCapacity = 16; });
            const int total = 1500;
            const int seen = 100;

            for (int i = 0; i < seen; i++) await host.Service.BroadcastAsync(i, "e");
            long lastId = ((PingDropHub)host.Service).LastIssuedEventId;
            Assert.Equal(seen, lastId);

            var publisher = Task.Run(async () =>
            {
                for (int i = seen; i < total; i++)
                {
                    await host.Service.BroadcastAsync(i, "e");
                    if (i % 7 == 0) await Task.Yield();
                }
            });

            await using var sse = await host.ConnectAsync(lastEventIdHeader: lastId.ToString());
            await publisher;

            var ids = await ReadIdsAsync(sse, total - seen);

            Assert.Equal(Enumerable.Range(seen + 1, total - seen).Select(i => (long)i), ids);
            await AssertNothingMoreAsync(sse);
        }
    }

    [Fact]
    public async Task ConcurrentPublishers_DuringConnect_KeepIdsStrictlyIncreasing()
    {
        await using var host = await PingDropHost.StartAsync(o => { o.EventHistorySize = 100_000; o.ClientQueueCapacity = 100_000; });
        const int perPublisher = 500;
        await host.Service.BroadcastAsync("first", "e"); // id 1, nobody connected: recorded for replay only

        var publishers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            for (int i = 0; i < perPublisher; i++) { await host.Service.BroadcastAsync(i, "e"); await Task.Yield(); }
        })).ToArray();

        await using var sse = await host.ConnectAsync(lastEventIdHeader: "0");
        await Task.WhenAll(publishers);

        var ids = await ReadIdsAsync(sse, 1 + 4 * perPublisher);
        Assert.Equal(Enumerable.Range(1, 1 + 4 * perPublisher).Select(i => (long)i), ids);
    }
}
