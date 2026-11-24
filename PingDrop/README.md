# PingDrop

Lightweight real-time notifications for ASP.NET Core, built on Server-Sent Events (SSE). No client library and no hub protocol: browsers connect with the built-in `EventSource`.

## Install

```
dotnet add package PingDrop
```

## Quick start

```csharp
using PingDrop.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddPingDrop();
var app = builder.Build();
app.MapPingDrop();   // SSE endpoint at /pingdrop/stream
app.Run();
```

## Browser client

```js
const es = new EventSource("/pingdrop/stream");
es.addEventListener("notification", e => console.log(JSON.parse(e.data)));
```

Event names match the `eventType` you send (default `"message"`); only the default `message` type reaches `onmessage`. To receive a subset, connect with `/pingdrop/stream?eventType=notification,alert`.

## Sending notifications

Inject `IPingDropService`:

```csharp
app.MapPost("/notify", async (IPingDropService pingDrop) =>
{
    // Everyone connected
    await pingDrop.BroadcastAsync(new { Text = "Deploy finished" }, "notification");

    // One subscriber, identified by key
    await pingDrop.SendToAsync("user-42", new { Text = "Hi" }, "notification");
});
```

Without a `TargetKeyResolver`, connections have no key and only `BroadcastAsync` reaches them. Subscribers are identified by `TargetKeyResolver`, a function over the `HttpContext`. Prefer deriving the key from authenticated claims rather than the query string, otherwise any client can subscribe as any user:

```csharp
builder.Services.AddPingDrop(o =>
{
    o.TargetKeyResolver = ctx => ctx.User.FindFirst("sub")?.Value;
});
```

## Options (`PingDropOptions`)

| Option | Default | Description |
|---|---|---|
| `EndpointPath` | `/pingdrop/stream` | SSE endpoint path |
| `AuthorizationPolicy` | `null` | Authorization policy required to connect to the SSE endpoint; `null` means anonymous |
| `HeartbeatInterval` | 15 s | How often a `: ping` comment is sent to idle connections (zero disables) |
| `ClientRetryInterval` | 3 s | Reconnect delay hint sent to clients as SSE `retry:` (zero omits) |
| `EventHistorySize` | `50` | Recent events kept in memory for the dashboard (not replayed to clients) |
| `EnableDashboard` | `true` | Serve the built-in dashboard |
| `Dashboard.Title` | `PingDrop Dashboard` | Dashboard title |
| `Dashboard.AuthorizationPolicy` | `null` | Authorization policy required for every dashboard route |
| `Dashboard.AllowAnonymous` | `false` | Allow the dashboard without a policy outside Development |
| `ClientConnected` / `ClientDisconnected` | `null` | Async callbacks receiving the connection id |
| `TargetKeyResolver` | `null` | Resolves the subscriber key from `HttpContext` |

## Dashboard

With `EnableDashboard` on, a live dashboard is served next to the stream endpoint (by default `/pingdrop/stream/dashboard`). It can send notifications to any client, so it is secured by default: without `Dashboard.AuthorizationPolicy` it is only mapped in the Development environment (elsewhere it is skipped and a warning is logged) unless you set `Dashboard.AllowAnonymous = true`.

```csharp
builder.Services.AddAuthorization(a => a.AddPolicy("PingDropAdmins", p => p.RequireRole("admin")));
builder.Services.AddPingDrop(o =>
{
    o.AuthorizationPolicy = "PingDropAdmins";           // SSE endpoint
    o.Dashboard.AuthorizationPolicy = "PingDropAdmins"; // dashboard
    // o.Dashboard.AllowAnonymous = true;               // trusted networks only
});
```

Call `UseAuthentication()` and `UseAuthorization()` before `MapPingDrop()`.

## Limits

Single node, in-memory only: no backplane, no replay of missed events (`Last-Event-ID` is ignored), and each client has a 20-event queue that drops the oldest events when full. Behind nginx/IIS/Azure/Cloudflare, disable response buffering and keep proxy idle timeouts above `HeartbeatInterval`.

## Documentation

Full docs: [getting started](https://github.com/goldi0002/PingDrop/blob/main/docs/getting-started.md), [configuration](https://github.com/goldi0002/PingDrop/blob/main/docs/configuration.md), [authentication](https://github.com/goldi0002/PingDrop/blob/main/docs/authentication.md), [reverse proxy](https://github.com/goldi0002/PingDrop/blob/main/docs/reverse-proxy.md), [JavaScript client](https://github.com/goldi0002/PingDrop/blob/main/docs/javascript-client.md), [dashboard](https://github.com/goldi0002/PingDrop/blob/main/docs/dashboard.md), [limitations](https://github.com/goldi0002/PingDrop/blob/main/docs/limitations.md).

## License

MIT. Source: https://github.com/goldi0002/PingDrop
