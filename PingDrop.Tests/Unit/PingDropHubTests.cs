using Microsoft.Extensions.Logging;
using PingDrop.Abstractions;
using PingDrop.Internal;

namespace PingDrop.Tests.Unit;

public class PingDropHubTests
{
    private static (PingDropHub hub, PDConnectionStore store, PDEventHistoryStore history) Create()
    {
        var store = new PDConnectionStore();
        var history = new PDEventHistoryStore(10);
        return (new PingDropHub(store, history, new NotificationFactory()), store, history);
    }

    [Fact]
    public async Task Broadcast_ReachesAllClients_AndIsRecorded()
    {
        var (hub, store, history) = Create();
        var a = store.Add(null, "u1");
        var b = store.Add(null);

        await hub.BroadcastAsync(new { X = 1 }, "evt");

        Assert.Equal(1, a.PendingNotifications);
        Assert.Equal(1, b.PendingNotifications);
        Assert.Equal(1, history.Count);
        Assert.Null(history.GetAll().Single().TargetKey);
        Assert.Equal(1, hub.TotalEventsPublished);
        Assert.Equal(2, hub.AttemptedDeliveries);
        Assert.Equal(2, hub.SuccessfulDeliveries);
    }

    [Fact]
    public async Task Broadcast_WithOnlyDashboardConnection_IsNoOp()
    {
        var (hub, store, history) = Create();
        var dash = store.Add(null, InternalConstants.DashboardSubscriberKey);

        await hub.BroadcastAsync("x", "evt");

        Assert.Equal(0, dash.PendingNotifications);
        Assert.Equal(0, history.Count);
        Assert.Equal(0, hub.TotalEventsPublished);
        Assert.Equal(0, hub.ConnectedClientsCount);
    }

    [Fact]
    public async Task Broadcast_SkipsDashboardConnection()
    {
        var (hub, store, history) = Create();
        var a = store.Add(null, "u1");
        var dash = store.Add(null, InternalConstants.DashboardSubscriberKey);

        await hub.BroadcastAsync("x", "evt");

        Assert.Equal(1, a.PendingNotifications);
        Assert.Equal(0, dash.PendingNotifications);
        Assert.Equal(1, hub.ConnectedClientsCount);
        Assert.Equal(1, hub.AttemptedDeliveries);
    }

    [Fact]
    public async Task SendTo_DashboardKey_IsNoOp_ForSingleAndMultiKey()
    {
        var (hub, store, history) = Create();
        var dash = store.Add(null, InternalConstants.DashboardSubscriberKey);
        var a = store.Add(null, "u1");

        await hub.SendToAsync(InternalConstants.DashboardSubscriberKey, "x", "evt");
        await hub.SendToAsync(new[] { InternalConstants.DashboardSubscriberKey, "u1" }, "x", "evt");

        Assert.Equal(0, dash.PendingNotifications);
        Assert.Equal(1, a.PendingNotifications);
        Assert.Equal(1, hub.TotalEventsPublished);
    }

    [Fact]
    public async Task Broadcast_WithNoClients_IsNoOp()
    {
        var (hub, _, history) = Create();

        await hub.BroadcastAsync("x", "evt");

        Assert.Equal(0, hub.TotalEventsPublished);
        Assert.Equal(0, history.Count);
    }

    [Fact]
    public async Task SendTo_ReachesOnlyMatchingKey()
    {
        var (hub, store, _) = Create();
        var target = store.Add(null, "u1");
        var other = store.Add(null, "u2");

        await hub.SendToAsync("u1", "hi", "evt");

        Assert.Equal(1, target.PendingNotifications);
        Assert.Equal(0, other.PendingNotifications);
    }

    [Fact]
    public async Task SendTo_IsRecordedInHistory_WithTargetKey()
    {
        var (hub, store, history) = Create();
        store.Add(null, "u1");

        await hub.SendToAsync("u1", "hi", "evt");

        var e = Assert.Single(history.GetAll());
        Assert.Equal("u1", e.TargetKey);
        Assert.Equal("evt", e.EventType);
    }

    [Fact]
    public async Task BroadcastAndSendTo_AreBothRecorded()
    {
        var (hub, store, history) = Create();
        store.Add(null, "u1");

        await hub.BroadcastAsync("a", "b");
        await hub.SendToAsync("u1", "c", "t");

        var all = history.GetAll().ToList();
        Assert.Equal(2, all.Count);
        Assert.Null(all[0].TargetKey);
        Assert.Equal("u1", all[1].TargetKey);
    }

    [Fact]
    public async Task SendTo_UnknownKey_IsNoOp()
    {
        var (hub, store, history) = Create();
        store.Add(null, "u1");

        await hub.SendToAsync("nobody", "hi", "evt");

        Assert.Equal(0, hub.TotalEventsPublished);
        Assert.Equal(0, history.Count);
    }

    [Fact]
    public async Task Delivery_RespectsEventTypeFilter()
    {
        var (hub, store, _) = Create();
        var filtered = store.Add(new HashSet<string> { "only" });
        var open = store.Add(null);

        await hub.BroadcastAsync("x", "other");

        Assert.Equal(0, filtered.PendingNotifications);
        Assert.Equal(1, open.PendingNotifications);
        Assert.Equal(1, hub.AttemptedDeliveries);
    }

    [Fact]
    public async Task SendTo_BlankKey_Throws()
    {
        var (hub, _, _) = Create();

        await Assert.ThrowsAsync<ArgumentException>(async () => await hub.SendToAsync(" ", "x", "evt"));
    }

    // ---- SendTo (multiple keys) ----

    [Fact]
    public async Task SendToMany_ReachesAllConnectionsOfAllKeys_AsOneEvent()
    {
        var (hub, store, history) = Create();
        var a1 = store.Add(null, "u1");
        var a2 = store.Add(null, "u1");
        var b = store.Add(null, "u2");
        var other = store.Add(null, "u3");

        await hub.SendToAsync(new[] { "u1", "u2" }, "hi", "evt");

        Assert.Equal(1, a1.PendingNotifications);
        Assert.Equal(1, a2.PendingNotifications);
        Assert.Equal(1, b.PendingNotifications);
        Assert.Equal(0, other.PendingNotifications);
        Assert.Equal(1, hub.TotalEventsPublished);
        Assert.Equal(3, hub.AttemptedDeliveries);
        Assert.Equal(3, hub.SuccessfulDeliveries);
        var e = Assert.Single(history.GetAll());
        Assert.Equal("evt", e.EventType);
        Assert.Equal("u1,u2", e.TargetKey);
    }

    [Fact]
    public async Task SendToMany_DedupesKeysAndIgnoresBlank()
    {
        var (hub, store, _) = Create();
        var a = store.Add(null, "u1");

        await hub.SendToAsync(new[] { "u1", "u1", "", "  ", null! }, "hi", "evt");

        Assert.Equal(1, a.PendingNotifications);
        Assert.Equal(1, hub.AttemptedDeliveries);
    }

    [Fact]
    public async Task SendToMany_RespectsEventTypeFilter()
    {
        var (hub, store, _) = Create();
        var filtered = store.Add(new HashSet<string> { "other" }, "u1");
        var matching = store.Add(new HashSet<string> { "evt" }, "u2");

        await hub.SendToAsync(new[] { "u1", "u2" }, "hi", "evt");

        Assert.Equal(0, filtered.PendingNotifications);
        Assert.Equal(1, matching.PendingNotifications);
        Assert.Equal(1, hub.AttemptedDeliveries);
        Assert.Equal(1, hub.TotalEventsPublished);
    }

    [Fact]
    public async Task SendToMany_UnknownKeys_IsNoOp()
    {
        var (hub, store, history) = Create();
        store.Add(null, "u1");

        await hub.SendToAsync(new[] { "nobody", "ghost" }, "hi", "evt");

        Assert.Equal(0, hub.TotalEventsPublished);
        Assert.Equal(0, history.Count);
    }

    [Fact]
    public async Task SendToMany_InvalidArguments_Throw()
    {
        var (hub, _, _) = Create();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await hub.SendToAsync((IEnumerable<string>)null!, "x"));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await hub.SendToAsync(new[] { "u1" }, (string)null!));
        await Assert.ThrowsAsync<ArgumentException>(async () => await hub.SendToAsync(Array.Empty<string>(), "x"));
        await Assert.ThrowsAsync<ArgumentException>(async () => await hub.SendToAsync(new[] { " ", "" }, "x"));
    }

    [Fact]
    public async Task SendTo_StringKeyStillBindsToSingleKeyOverload()
    {
        var (hub, store, history) = Create();
        var a = store.Add(null, "u1");
        var obj = new { X = 1 };

        await hub.SendToAsync("u1", obj);
        await hub.SendToAsync("u1", "payload", "evt");
        IPingDropService svc = hub;
        await svc.SendToAsync("u1", obj, "evt");

        Assert.Equal(3, a.PendingNotifications);
        Assert.All(history.GetAll(), e => Assert.Equal("u1", e.TargetKey));
    }

    [Fact]
    public async Task DroppedDeliveries_AreLoggedOnceAsWarning_Aggregated()
    {
        var logs = new LogCollector();
        var store = new PDConnectionStore();
        var hub = new PingDropHub(store, new PDEventHistoryStore(10), new NotificationFactory(), LoggerFactory.Create(b => b.AddProvider(logs)));
        var a = store.Add(null);
        var b = store.Add(null);
        await a.DisposeAsync();
        await b.DisposeAsync();

        await hub.BroadcastAsync("x", "evt");
        await hub.BroadcastAsync("y", "evt");

        var warn = Assert.Single(logs.Entries, e => e.Level == LogLevel.Warning);
        Assert.Equal("PingDrop", warn.Category);
        Assert.Contains("Dropped 2 of 2", warn.Message);
        Assert.Equal(4, hub.DroppedDeliveries);
    }

    [Fact]
    public async Task QueueOverflow_IsCountedInHubMetrics_AndWarnsOnce()
    {
        var logs = new LogCollector();
        var store = new PDConnectionStore();
        var hub = new PingDropHub(store, new PDEventHistoryStore(100), new NotificationFactory(), LoggerFactory.Create(b => b.AddProvider(logs)));
        var slow = store.Add(null, "slow");
        int sends = PDClientConnection.QueueCapacity + 5;

        for (int i = 0; i < sends; i++)
            await hub.BroadcastAsync(i, "evt");

        Assert.Equal(5, slow.NotificationsDropped);
        Assert.Equal(PDClientConnection.QueueCapacity, slow.PendingNotifications);

        var m = hub.GetMetrics();
        Assert.Equal(sends, m.AttemptedDeliveries);
        Assert.Equal(5, m.DroppedDeliveries);
        Assert.Equal(PDClientConnection.QueueCapacity, m.SuccessfulDeliveries);
        Assert.Equal(m.AttemptedDeliveries, m.SuccessfulDeliveries + m.DroppedDeliveries);
        Assert.Equal(5, hub.DroppedDeliveries);

        var warn = Assert.Single(logs.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("Dropped 1 of 1", warn.Message);
    }
}
