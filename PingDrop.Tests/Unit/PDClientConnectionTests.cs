using PingDrop.Abstractions;
using PingDrop.Internal;
using PingDrop.Models;

namespace PingDrop.Tests.Unit;

public class PDClientConnectionTests
{
    private static PDNotification N(long id) => new() { Id = id, EventType = "e", Payload = "{}" };

    [Fact]
    public void IsSubscribedTo_NoFilter_AcceptsEverything()
    {
        var c = new PDClientConnection(null);

        Assert.False(c.HasSubscriptions);
        Assert.True(c.IsSubscribedTo("anything"));
    }

    [Fact]
    public void IsSubscribedTo_WithFilter_MatchesOnlyListed()
    {
        var c = new PDClientConnection(new HashSet<string> { "a" });

        Assert.True(c.HasSubscriptions);
        Assert.True(c.IsSubscribedTo("a"));
        Assert.False(c.IsSubscribedTo("b"));
    }

    [Fact]
    public void FullChannel_DropsOldest_AndKeepsLatest()
    {
        var c = new PDClientConnection(null);

        for (long i = 1; i <= 25; i++)
            Assert.True(c.TryWriteNotification(N(i)));

        Assert.Equal(20, c.PendingNotifications);
        Assert.True(c.NotificationChannel.Reader.TryRead(out var first));
        Assert.Equal(6, first!.Id);
    }

    [Fact]
    public async Task Dispose_CompletesChannel_AndRejectsWrites()
    {
        var c = new PDClientConnection(null);

        await c.DisposeAsync();

        Assert.True(c.IsDisposed);
        Assert.False(c.TryWriteNotification(N(1)));
        Assert.True(c.NotificationChannel.Reader.Completion.IsCompleted);
    }

    [Fact]
    public void Defaults_AreCapacity20_DropOldest()
    {
        var options = new PingDropOptions();
        Assert.Equal(20, options.ClientQueueCapacity);
        Assert.Equal(PingDropQueueFullMode.DropOldest, options.ClientQueueFullMode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ClientQueueCapacity_BelowOne_Throws(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PingDropOptions().ClientQueueCapacity = value);
    }

    [Fact]
    public void DropOldest_CountsDisplacedNotifications()
    {
        var c = new PDClientConnection(null, null, 3, PingDropQueueFullMode.DropOldest);

        for (long i = 1; i <= 5; i++)
            Assert.True(c.TryWriteNotification(N(i), out int dropped) && (dropped == (i > 3 ? 1 : 0)));

        Assert.Equal(2, c.NotificationsDropped);
        Assert.Equal(3, c.PendingNotifications);
        Assert.True(c.NotificationChannel.Reader.TryRead(out var first));
        Assert.Equal(3, first!.Id);
        Assert.False(c.IsDisposed);
    }

    [Fact]
    public void DropNewest_KeepsQueued_AndCountsRejected()
    {
        var c = new PDClientConnection(null, null, 3, PingDropQueueFullMode.DropNewest);

        for (long i = 1; i <= 3; i++)
            Assert.True(c.TryWriteNotification(N(i)));
        Assert.False(c.TryWriteNotification(N(4), out int dropped));
        Assert.False(c.TryWriteNotification(N(5)));

        Assert.Equal(1, dropped);
        Assert.Equal(2, c.NotificationsDropped);
        Assert.Equal(3, c.PendingNotifications);
        Assert.True(c.NotificationChannel.Reader.TryRead(out var first));
        Assert.Equal(1, first!.Id);
        Assert.False(c.IsDisposed);
    }

    [Fact]
    public void DisconnectClient_CompletesConnection_WhenFull()
    {
        var c = new PDClientConnection(null, null, 2, PingDropQueueFullMode.DisconnectClient);

        Assert.True(c.TryWriteNotification(N(1)));
        Assert.True(c.TryWriteNotification(N(2)));
        Assert.False(c.TryWriteNotification(N(3)));

        Assert.True(c.IsDisposed);
        Assert.Equal(1, c.NotificationsDropped);
        // Queued items are still readable, then the channel completes.
        Assert.True(c.NotificationChannel.Reader.TryRead(out _));
        Assert.True(c.NotificationChannel.Reader.TryRead(out _));
        Assert.True(c.NotificationChannel.Reader.Completion.IsCompleted);
    }

    [Fact]
    public void Store_AppliesOptionsToNewConnections()
    {
        var store = new PDConnectionStore(2, PingDropQueueFullMode.DropNewest);
        var c = store.Add(null, "k");

        Assert.True(c.TryWriteNotification(N(1)));
        Assert.True(c.TryWriteNotification(N(2)));
        Assert.False(c.TryWriteNotification(N(3)));
    }
}
