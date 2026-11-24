# Getting started

## Install

```
dotnet add package PingDrop
```

PingDrop targets `net8.0` and uses the ASP.NET Core shared framework. Nothing else is required.

## Wire it up

```csharp
using PingDrop.Abstractions;
using PingDrop.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddPingDrop();      // optional: AddPingDrop(o => { ... })

var app = builder.Build();
app.MapPingDrop();                   // maps GET /pingdrop/stream (+ dashboard in Development)
app.Run();
```

`AddPingDrop` registers PingDrop's services as singletons, including `IPingDropService`. `MapPingDrop` maps the SSE endpoint at `PingDropOptions.EndpointPath` (default `/pingdrop/stream`) and, if `EnableDashboard` is true, the dashboard under `<EndpointPath>/dashboard`.

## Send notifications

Inject `IPingDropService`. It has two methods:

```csharp
ValueTask BroadcastAsync<T>(T notification, string eventType = "message", CancellationToken ct = default);
ValueTask SendToAsync<T>(string targetKey, T notification, string eventType = "message", CancellationToken ct = default);
```

```csharp
app.MapPost("/notify", async (IPingDropService pingDrop) =>
{
    // Every connected client
    await pingDrop.BroadcastAsync(new { Text = "Deploy finished" }, "notification");

    // Clients whose subscriber key is "user-42"
    await pingDrop.SendToAsync("user-42", new { Text = "Hi" }, "notification");

    return Results.Accepted();
});
```

Notes on behaviour:

- The notification object is serialized to a single JSON string with `System.Text.Json` (default options) and sent as the SSE `data:` field.
- `eventType` becomes the SSE `event:` name. The browser must listen for that name (see [JavaScript client](javascript-client.md)). The default is `message`.
- Sending is fire-and-forget into each client's in-memory queue. The returned task completes before the bytes are written to the network; nothing tells you whether a client received the event.
- If nobody is connected (for `BroadcastAsync`) or no client has the key (for `SendToAsync`), the call returns without doing anything.
- `SendToAsync` throws `ArgumentException` for a null/blank `targetKey`; both methods throw `ArgumentNullException` for a null notification.

## Identify subscribers

By default clients have no key, so only `BroadcastAsync` reaches them. To use `SendToAsync`, tell PingDrop how to derive a key from the connecting request:

```csharp
builder.Services.AddPingDrop(o =>
{
    o.TargetKeyResolver = ctx => ctx.User.FindFirst("sub")?.Value;
});
```

Several connections (several tabs, several devices) can share one key; `SendToAsync` delivers to all of them. Do not take the key from the query string in production, because any client could then subscribe as any user. See [Authentication](authentication.md).

## Receive in the browser

```js
const es = new EventSource("/pingdrop/stream");
es.addEventListener("notification", e => console.log(JSON.parse(e.data)));
```

Full details, including event-type filtering and reconnects, are in [JavaScript client](javascript-client.md).

## Check the dashboard

In the Development environment the dashboard is available at `/pingdrop/stream/dashboard`. In any other environment it is not mapped unless you secure it or opt in. See [Dashboard](dashboard.md).

## Next steps

- [Samples](../samples/README.md): minimal API, MVC, Blazor and background-sender apps you can run
- [Configuration](configuration.md) for all options
- [Authentication](authentication.md) before going to production
- [Reverse proxy](reverse-proxy.md) if you deploy behind nginx, IIS, Azure or Cloudflare
- [Limitations](limitations.md)
