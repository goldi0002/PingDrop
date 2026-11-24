# Configuration

Options are set through the delegate passed to `AddPingDrop`:

```csharp
builder.Services.AddPingDrop(o =>
{
    o.EndpointPath = "/events";
    o.HeartbeatInterval = TimeSpan.FromSeconds(15);
});
```

The delegate runs once, when `AddPingDrop` is called. Options are read from the resulting singleton; `IOptions<T>` and configuration binding are not used.

## `PingDropOptions`

| Property | Type | Default | Description |
|---|---|---|---|
| `EndpointPath` | `string` | `/pingdrop/stream` | Path of the SSE endpoint. The dashboard lives under `<EndpointPath>/dashboard`. |
| `AuthorizationPolicy` | `string?` | `null` | Name of an authorization policy required to connect to the SSE endpoint. `null` means the endpoint is anonymous. |
| `HeartbeatInterval` | `TimeSpan` | 15 seconds | How often a `: ping` comment is written to an idle connection, so proxies and load balancers do not close it and dead peers are noticed. Zero or negative disables heartbeats. |
| `ClientRetryInterval` | `TimeSpan` | 3 seconds | Reconnect delay suggested to clients with an SSE `retry:` field, sent once when a stream opens. Zero or negative omits the hint (the client uses its own default). |
| `EventHistorySize` | `int` | `50` | Number of recent events kept in memory. Used by the dashboard only; it is not replayed to clients. |
| `EnableDashboard` | `bool` | `true` | Whether to map the dashboard. See [Dashboard](dashboard.md) for the Development-only default. |
| `Dashboard` | `PingDropDashboardOptions` | new instance | Dashboard options, below. |
| `ClientConnected` | `Func<long, Task>?` | `null` | Called after a client connects, with its connection id. Exceptions thrown by the callback are swallowed. |
| `ClientDisconnected` | `Func<long, Task>?` | `null` | Called after a client disconnects, with its connection id. Exceptions are swallowed. |
| `TargetKeyResolver` | `Func<HttpContext, string?>?` | `null` | Maps the SSE request to a subscriber key (or `null` for none). Required for `SendToAsync` to reach anyone. |

### `AuthorizationPolicy`

The policy must be defined with `AddAuthorization`, and the pipeline must run authentication and authorization before the endpoint, otherwise requests fail. See [Authentication](authentication.md).

### `HeartbeatInterval`

Heartbeats are SSE comments (`: ping`), which `EventSource` ignores. They refresh the connection's "last activity" time shown on the dashboard but are not counted as delivered notifications. Keep the interval comfortably below the shortest idle timeout of anything between the server and the browser; see [Reverse proxy](reverse-proxy.md).

### `ClientRetryInterval`

Sent as `retry: <milliseconds>` at the start of every stream. Browsers use it as the delay before reconnecting after the connection drops. It does not cause or prevent reconnects.

### `EventHistorySize`

A fixed-size ring buffer. Values below 1 are treated as 1. An event is recorded only when at least one connection exists to consider it: a broadcast with no connections, or a `SendToAsync` for a key nobody holds, is not recorded.

### `TargetKeyResolver`

Runs once per SSE connection. Multiple connections can resolve to the same key. Keys are compared case-sensitively. Returning `null` or an empty string leaves the connection keyless: it still receives broadcasts.

### `ClientConnected` / `ClientDisconnected`

The id is a process-local `long` (it is the same id the dashboard shows). It is not stable across restarts or across reconnects of the same browser.

## `PingDropOptions.Dashboard` (`PingDropDashboardOptions`)

| Property | Type | Default | Description |
|---|---|---|---|
| `Title` | `string` | `PingDrop Dashboard` | Title shown on the dashboard page. |
| `AuthorizationPolicy` | `string?` | `null` | Policy required for every dashboard route (page, assets, API, live feed), including sending notifications. |
| `AllowAnonymous` | `bool` | `false` | Allows mapping the dashboard with no policy outside Development. Trusted networks only. |
| `Path` | `string` (read-only) | `<EndpointPath>/dashboard` | The dashboard's base path. Derived from `EndpointPath`; you cannot set it separately. |

When `Dashboard.AuthorizationPolicy` is `null` and `AllowAnonymous` is `false`, the dashboard is mapped only if the host environment is Development. In any other environment it is skipped and a warning is logged at startup. See [Authentication](authentication.md#securing-the-dashboard).

## Example

```csharp
builder.Services.AddPingDrop(o =>
{
    o.EndpointPath = "/events";
    o.AuthorizationPolicy = "PingDropClients";
    o.TargetKeyResolver = ctx => ctx.User.FindFirst("sub")?.Value;

    o.HeartbeatInterval = TimeSpan.FromSeconds(15);
    o.ClientRetryInterval = TimeSpan.FromSeconds(3);
    o.EventHistorySize = 100;

    o.ClientConnected = id => { Console.WriteLine($"connected {id}"); return Task.CompletedTask; };
    o.ClientDisconnected = id => Task.CompletedTask;

    o.EnableDashboard = true;
    o.Dashboard.Title = "Acme notifications";
    o.Dashboard.AuthorizationPolicy = "PingDropAdmins";
});
```

## Not configurable

These are fixed in the current version: the per-client queue size (20 events, oldest dropped when full), the query parameter used for event filtering (`eventType`), and the SSE event id scheme. See [Limitations](limitations.md).
