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
}
