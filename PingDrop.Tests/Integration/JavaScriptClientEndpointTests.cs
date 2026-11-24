using System.Net;

namespace PingDrop.Tests.Integration;

public class JavaScriptClientEndpointTests
{
    [Fact]
    public async Task ClientJs_IsServedWithJavaScriptContentType()
    {
        await using var host = await PingDropHost.StartAsync();

        var res = await host.Client.GetAsync("/pingdrop/stream/client.js");
        var body = await res.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("text/javascript", res.Content.Headers.ContentType?.MediaType);
        Assert.False(string.IsNullOrWhiteSpace(body));
        Assert.Contains("PingDropClient", body);
        Assert.True(res.Headers.CacheControl?.Public);
    }

    [Fact]
    public async Task ClientJs_Disabled_Returns404()
    {
        await using var host = await PingDropHost.StartAsync(o => o.ServeJavaScriptClient = false);

        var res = await host.Client.GetAsync("/pingdrop/stream/client.js");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
