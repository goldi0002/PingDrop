using Microsoft.AspNetCore.Http;
using PingDrop.Internal;
using PingDrop.Models;
using System.Text;

namespace PingDrop.Tests.Unit;

public class PDSseWriterTests
{
    [Fact]
    public async Task WriteAsync_EmitsIdEventAndDataFrame()
    {
        var ctx = new DefaultHttpContext();
        var body = new MemoryStream();
        ctx.Response.Body = body;

        await PDSseWriter.WriteAsync(ctx.Response, new PDNotification { Id = 7, EventType = "alert", Payload = "{\"a\":1}" });

        Assert.Equal("id: 7\nevent: alert\ndata: {\"a\":1}\n\n", Encoding.UTF8.GetString(body.ToArray()));
    }
}
