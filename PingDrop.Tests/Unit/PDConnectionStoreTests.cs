using PingDrop.Internal;

namespace PingDrop.Tests.Unit;

public class PDConnectionStoreTests
{
    [Fact]
    public void Add_WithKey_IsIndexedByKeyAndCounted()
    {
        var store = new PDConnectionStore();

        var a = store.Add(null, "user1");
        var b = store.Add(null, "user1");
        store.Add(null, "user2");

        Assert.Equal(3, store.Count);
        Assert.Equal(2, store.CountByKey("user1"));
        Assert.Equal(new[] { a.ConnectionId, b.ConnectionId }.Order(), store.GetByKey("user1").Select(c => c.ConnectionId).Order());
    }

    [Fact]
    public void Add_WithoutKey_IsOnlyInAll()
    {
        var store = new PDConnectionStore();

        var c = store.Add(null);

        Assert.Equal(1, store.Count);
        Assert.Null(c.SubscriberKey);
        Assert.Empty(store.GetByKey(""));
    }

    [Fact]
    public void Remove_DropsConnectionAndEmptyKeyBucket()
    {
        var store = new PDConnectionStore();
        var c = store.Add(null, "user1");

        store.Remove(c.ConnectionId);

        Assert.Equal(0, store.Count);
        Assert.Equal(0, store.CountByKey("user1"));
        Assert.Empty(store.GetByKey("user1"));
    }

    [Fact]
    public void Remove_UnknownId_IsNoOp()
    {
        var store = new PDConnectionStore();
        store.Add(null, "user1");

        store.Remove(-1);

        Assert.Equal(1, store.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   \t")]
    public void Add_BlankKey_IsNotIndexedAndHasNullKey(string blank)
    {
        var store = new PDConnectionStore();

        var c = store.Add(null, blank);

        Assert.Null(c.SubscriberKey);
        Assert.Equal(1, store.Count);
        Assert.Equal(0, store.CountByKey(blank));
        Assert.Empty(store.GetByKey(blank));
    }

    [Fact]
    public void Remove_BlankKey_LeavesNoLeftovers()
    {
        var store = new PDConnectionStore();
        var a = store.Add(null, " ");
        store.Remove(a.ConnectionId);

        var b = store.Add(null, " ");

        Assert.Equal(1, store.Count);
        Assert.Equal(0, store.CountByKey(" "));
        Assert.Empty(store.GetByKey(" "));
        store.Remove(b.ConnectionId);
        Assert.Equal(0, store.Count);
    }
}
