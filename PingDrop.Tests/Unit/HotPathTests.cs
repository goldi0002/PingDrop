using Microsoft.AspNetCore.Http;
using PingDrop.Abstractions;
using PingDrop.Internal;
using PingDrop.Models;
using PingDrop.Tests.Integration;
using System.Text;

namespace PingDrop.Tests.Unit;

/// <summary>Covers the allocation-free publish path: cached frames, snapshot arrays, recipient resolution, reused heartbeat source.</summary>
public class HotPathTests
{
    private static string Frame(PDNotification n) => Encoding.UTF8.GetString(PDSseWriter.GetFrame(n));

    [Fact]
    public void GetFrame_IsBuiltOnce_AndSharedAcrossWrites()
    {
        var n = new PDNotification { Id = 12, EventType = "e", Payload = "{}" };

        var first = PDSseWriter.GetFrame(n);

        Assert.Same(first, PDSseWriter.GetFrame(n));
        Assert.Equal("id: 12\nevent: e\ndata: {}\n\n", Frame(n));
    }

    [Fact]
    public void GetFrame_MatchesTheOldFormatting_ForNullsNonAsciiAndLargeIds()
    {
        Assert.Equal("id: 0\nevent: \ndata: \n\n", Frame(new PDNotification()));
        Assert.Equal("id: 9223372036854775807\nevent: café\ndata: {\"m\":\"ü€😀\"}\n\n",
            Frame(new PDNotification { Id = long.MaxValue, EventType = "café", Payload = "{\"m\":\"ü€😀\"}" }));
    }

    [Fact]
    public async Task WriteAsync_TwoClientsReceiveIdenticalBytes_FromOneFrame()
    {
        var n = new PDNotification { Id = 3, EventType = "e", Payload = "\"x\"" };
        var bodies = new List<MemoryStream>();
        for (int i = 0; i < 2; i++)
        {
            var ctx = new DefaultHttpContext();
            var body = new MemoryStream();
            ctx.Response.Body = body;
            bodies.Add(body);
            await PDSseWriter.WriteAsync(ctx.Response, n);
        }

        Assert.Equal(bodies[0].ToArray(), bodies[1].ToArray());
        Assert.Equal("id: 3\nevent: e\ndata: \"x\"\n\n", Encoding.UTF8.GetString(bodies[0].ToArray()));
    }

    [Fact]
    public void Store_SnapshotsAreImmutable_AndTrackConnectAndDisconnect()
    {
        var store = new PDConnectionStore();
        var a = store.Add(null, "k");
        var dash = store.Add(null, InternalConstants.DashboardSubscriberKey);

        var before = store.GetAll();
        var beforeKey = store.GetByKey("k");
        var b = store.Add(null, "k");

        Assert.Equal([a], before);
        Assert.Equal([a], beforeKey);
        Assert.Equal([a, b], store.GetAll());
        Assert.Equal([a, b], store.GetByKey("k"));
        Assert.DoesNotContain(dash, store.GetAll());
        Assert.Equal([dash], store.GetByKey(InternalConstants.DashboardSubscriberKey));

        store.Remove(a.ConnectionId);
        store.Remove(dash.ConnectionId);

        Assert.Equal([b], store.GetAll());
        Assert.Equal([b], store.GetByKey("k"));
        Assert.Equal(1, store.CountByKey("k"));

        store.Remove(b.ConnectionId);
        store.Remove(b.ConnectionId); // removing twice is a no-op

        Assert.Empty(store.GetAll());
        Assert.Empty(store.GetByKey("k"));
        Assert.Equal(0, store.CountByKey("k"));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task MultiKeySend_ReachesEveryConnectionOfEveryKeyOnce()
    {
        var store = new PDConnectionStore();
        var hub = new PingDropHub(store, new PDEventHistoryStore(10), new NotificationFactory());
        var a1 = store.Add(null, "a");
        var a2 = store.Add(null, "a");
        var b1 = store.Add(null, "b");
        var c1 = store.Add(null, "c");
        var none = store.Add(null);

        await hub.SendToAsync(new[] { "a", "b", "missing" }, "hi", "evt");

        Assert.Equal([1, 1, 1, 0, 0], new[] { a1, a2, b1, c1, none }.Select(c => c.PendingNotifications));
        Assert.Equal(3, hub.AttemptedDeliveries);
        Assert.Equal(1, hub.TotalEventsPublished);
    }

    [Fact]
    public async Task MultiKeySend_WithOneConnectedKey_ReachesIt()
    {
        var store = new PDConnectionStore();
        var hub = new PingDropHub(store, new PDEventHistoryStore(10), new NotificationFactory());
        var b1 = store.Add(null, "b");

        await hub.SendToAsync(new[] { "a", "b" }, "hi", "evt");

        Assert.Equal(1, b1.PendingNotifications);
    }

    [Fact]
    public async Task Publish_WithNoRecipients_DoesNotConsumeAnId()
    {
        var store = new PDConnectionStore();
        var factory = new NotificationFactory();
        var hub = new PingDropHub(store, new PDEventHistoryStore(10), factory);

        await hub.BroadcastAsync("x");
        await hub.SendToAsync("nobody", "x");
        await hub.SendToAsync(new[] { "a", "b" }, "x");

        Assert.Equal(0, factory.LastIssuedId);
    }

    [Fact]
    public async Task HeartbeatWaiter_PingsWhenIdle_ReturnsOnEvent_AndPingsAgain()
    {
        var ctx = new DefaultHttpContext();
        var body = new MemoryStream();
        ctx.Response.Body = body;
        var client = new PDClientConnection();
        using var waiter = new PDHeartbeatWaiter(client.NotificationChannel.Reader, ctx.Response, TimeSpan.FromMilliseconds(40), client, CancellationToken.None);

        // An event that is already queued returns at once without a ping, and re-arms the same source.
        client.TryWriteNotification(new PDNotification { Id = 1 });
        Assert.True(await waiter.WaitToReadAsync());
        Assert.Empty(body.ToArray());
        client.NotificationChannel.Reader.TryRead(out _);

        // Idle: at least two pings arrive before the event does, even though the first timeout cancels the source.
        var wait = waiter.WaitToReadAsync().AsTask();
        await PingDropHost.WaitUntilAsync(() => Encoding.UTF8.GetString(body.ToArray()) == ": ping\n\n: ping\n\n");
        client.TryWriteNotification(new PDNotification { Id = 2 });
        Assert.True(await wait.WaitAsync(TimeSpan.FromSeconds(5)));

        // Reused after the event too.
        client.NotificationChannel.Reader.TryRead(out _);
        client.TryWriteNotification(new PDNotification { Id = 3 });
        Assert.True(await waiter.WaitToReadAsync());
    }

    [Fact]
    public async Task HeartbeatWaiter_CallerCancellation_Propagates_AndZeroIntervalDisablesPings()
    {
        var ctx = new DefaultHttpContext();
        var body = new MemoryStream();
        ctx.Response.Body = body;
        var client = new PDClientConnection();
        using var cts = new CancellationTokenSource();
        using var waiter = new PDHeartbeatWaiter(client.NotificationChannel.Reader, ctx.Response, TimeSpan.Zero, client, cts.Token);

        var wait = waiter.WaitToReadAsync().AsTask();
        await Task.Delay(100);
        Assert.Empty(body.ToArray());
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);

        using var cts2 = new CancellationTokenSource();
        using var timed = new PDHeartbeatWaiter(client.NotificationChannel.Reader, ctx.Response, TimeSpan.FromSeconds(30), client, cts2.Token);
        var wait2 = timed.WaitToReadAsync().AsTask();
        cts2.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait2);
    }

    [Fact]
    public async Task EmbeddedFiles_AreReadOnce()
    {
        string first = await PDEmbeddedFileProvider.ReadAsync("app.css");

        Assert.NotEmpty(first);
        Assert.Same(first, await PDEmbeddedFileProvider.ReadAsync("app.css"));
    }
}
