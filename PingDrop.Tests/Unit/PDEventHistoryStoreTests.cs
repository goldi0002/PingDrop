using PingDrop.Internal;
using PingDrop.Models;

namespace PingDrop.Tests.Unit;

public class PDEventHistoryStoreTests
{
    private static PDNotification N(long id) => new() { Id = id, EventType = "e", Payload = "{}" };

    [Fact]
    public void GetAll_BelowCapacity_ReturnsInInsertionOrder()
    {
        var store = new PDEventHistoryStore(5);
        store.AddEvent(N(1));
        store.AddEvent(N(2));

        Assert.Equal(2, store.Count);
        Assert.Equal(new long[] { 1, 2 }, store.GetAll().Select(n => n.Id));
    }

    [Fact]
    public void AddEvent_BeyondCapacity_KeepsNewestAndOrder()
    {
        var store = new PDEventHistoryStore(3);
        for (long i = 1; i <= 5; i++) store.AddEvent(N(i));

        Assert.Equal(3, store.Count);
        Assert.Equal(new long[] { 3, 4, 5 }, store.GetAll().Select(n => n.Id));
    }

    [Fact]
    public void GetRecentEvents_ReturnsLastN()
    {
        var store = new PDEventHistoryStore(10);
        for (long i = 1; i <= 5; i++) store.AddEvent(N(i));

        Assert.Equal(new long[] { 4, 5 }, store.GetRecentEvents(2).Select(n => n.Id));
        Assert.Empty(store.GetRecentEvents(0));
    }

    [Fact]
    public void Capacity_IsClampedToAtLeastOne()
    {
        var store = new PDEventHistoryStore(0);
        store.AddEvent(N(1));
        store.AddEvent(N(2));

        Assert.Equal(new long[] { 2 }, store.GetAll().Select(n => n.Id));
    }
}
