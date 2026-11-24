# PingDrop

Lightweight real-time notifications for ASP.NET Core, built on Server-Sent Events (SSE).

PingDrop lets server code push events to browsers over a plain HTTP stream. There is no hub protocol and no client library: the browser connects with the built-in `EventSource`, and your server code calls `BroadcastAsync` or `SendToAsync`. A monitoring dashboard (connections, delivery counters, a test-send form) is included.

It targets `net8.0`. It is a good fit when you need one-way server-to-browser notifications (toasts, progress, "your report is ready") from a single ASP.NET Core instance. It is not a message broker: see [Limitations](docs/limitations.md) before you rely on it.

## Install

```
dotnet add package PingDrop
```

## Quick start (5 minutes)

**1. Register and map PingDrop** (`Program.cs`):

```csharp
using PingDrop.Abstractions;
using PingDrop.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddPingDrop();

var app = builder.Build();
app.MapPingDrop();   // SSE endpoint at /pingdrop/stream

app.MapPost("/notify", async (IPingDropService pingDrop) =>
{
    await pingDrop.BroadcastAsync(new { Text = "Deploy finished" }, "notification");
    return Results.Accepted();
});

app.Run();
```

**2. Listen in the browser:**

```js
const es = new EventSource("/pingdrop/stream");
es.addEventListener("notification", e => console.log(JSON.parse(e.data)));
```

**3. Send something:** `curl -X POST http://localhost:5000/notify` (use your app's port). The browser console logs `{ Text: "Deploy finished" }`.

**4. Open the dashboard** (Development only by default): `http://localhost:5000/pingdrop/stream/dashboard`.

To send to one user instead of everyone, identify subscribers with `TargetKeyResolver` and call `SendToAsync`. See [Getting started](docs/getting-started.md) and [Authentication](docs/authentication.md).

## Documentation

| Page | Contents |
|---|---|
| [Getting started](docs/getting-started.md) | Install, wiring, broadcast vs targeted sends, event types |
| [Configuration](docs/configuration.md) | Every `PingDropOptions` and dashboard option, with defaults |
| [Authentication](docs/authentication.md) | Cookie and JWT setups, claims-based keys, securing the dashboard |
| [Reverse proxy](docs/reverse-proxy.md) | nginx, IIS/ARR, Azure, Cloudflare; heartbeats and timeouts |
| [JavaScript client](docs/javascript-client.md) | `EventSource` usage, event filtering, reconnects |
| [Dashboard](docs/dashboard.md) | Routes, the send-notification API, security |
| [Limitations](docs/limitations.md) | Single node, in-memory, no replay, queue behaviour |

## License

MIT. Source: https://github.com/goldi0002/PingDrop
