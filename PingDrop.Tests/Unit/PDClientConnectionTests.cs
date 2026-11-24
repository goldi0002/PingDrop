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
    public void Overflow_IsCountedAsDropped_AndLatestEventsRetained()
    {
        var c = new PDClientConnection(null);
        int total = PDClientConnection.QueueCapacity + 7;

        for (long i = 1; i <= total; i++)
            c.TryWriteNotification(N(i));

        Assert.Equal(7, c.NotificationsDropped);

        var ids = new List<long>();
        while (c.NotificationChannel.Reader.TryRead(out var n))
            ids.Add(n.Id);
        Assert.Equal(Enumerable.Range(8, PDClientConnection.QueueCapacity).Select(i => (long)i), ids);
    }

    [Fact]
    public void NoOverflow_NothingDropped()
    {
        var c = new PDClientConnection(null);

        for (long i = 1; i <= PDClientConnection.QueueCapacity; i++)
            c.TryWriteNotification(N(i));

        Assert.Equal(0, c.NotificationsDropped);
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
}
