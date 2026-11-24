using PingDrop.Internal;

namespace PingDrop.Tests.Unit;

public class NotificationFactoryTests
{
    [Fact]
    public void CreateNotification_SerializesPayload_AndAssignsIncreasingIds()
    {
        var f = new NotificationFactory();

        var a = f.CreateNotification(new { Msg = "hi" }, "evt", "k");
        var b = f.CreateNotification("x", "evt");

        Assert.Equal("{\"Msg\":\"hi\"}", a.Payload);
        Assert.Equal("evt", a.EventType);
        Assert.Equal("k", a.TargetKey);
        Assert.Equal(a.Id + 1, b.Id);
    }
}
