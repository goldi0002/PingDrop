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
| `EventHistorySize` | `int` | `50` | Number of recent events kept in memory. Feeds the dashboard and bounds how many missed events can be replayed to a reconnecting client. |
| `EnableReplay` | `bool` | `true` | Replay retained events a reconnecting client missed, using `Last-Event-ID` (header) or `lastEventId` (query). See [`EnableReplay`](#enablereplay). |
| `ClientQueueCapacity` | `int` | `20` | Maximum notifications queued per connected client. Must be at least 1; smaller values throw `ArgumentOutOfRangeException`. |
| `ClientQueueFullMode` | `PingDropQueueFullMode` | `DropOldest` | What happens when a client's queue is full: `DropOldest`, `DropNewest` or `DisconnectClient`. |
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

A fixed-size ring buffer. Values below 1 are treated as 1. With `EnableReplay` on (the default) every broadcast and send is recorded, even when nobody is connected, so a client can catch up after a gap. With `EnableReplay` off, an event is recorded only when at least one connection exists to consider it: a broadcast with no connections, or a `SendToAsync` for a key nobody holds, is not recorded.

### `EnableReplay`

When a client connects with a `Last-Event-ID` request header (sent automatically by `EventSource` when it reconnects) or a `lastEventId` query parameter (for clients that recreate the `EventSource` themselves; the header wins if both are present), the server first writes the retained events with a larger id, in id order, then continues with live events, with no duplicates and no gap. A client only gets what it would have received live: broadcasts, events sent to its subscriber key (including multi-key sends), filtered by its `eventType` list. Clients without a key get broadcasts only. The dashboard feed is never replayed.

Rules and limits:

- Replay is limited to the last `EventHistorySize` events and lives in memory only.
- Ids restart at 1 on every process start. If the supplied id is greater than the newest id the process has issued (typical after a restart), nothing is replayed; an invalid (non-numeric or negative) id is ignored.
- Replayed events are written straight to the response, not through the per-client queue, so a gap larger than `ClientQueueCapacity` is not lost. They count toward the client's delivery counters and the attempted/successful delivery metrics, but not toward "Events Published".
- Set `EnableReplay = false` to turn the feature off entirely (the header and parameter are then ignored and events are not recorded when nobody is connected).

### `ClientQueueCapacity` / `ClientQueueFullMode`

Every connection has its own bounded queue between `BroadcastAsync`/`SendToAsync` and the network write. A client that reads slowly lets it fill up. The mode decides what gives:

- `DropOldest` (default): the oldest queued event is discarded to make room, so the client ends up with the most recent events.
- `DropNewest`: the new event is discarded and the queued ones are kept, so the client sees events in order without gaps until it catches up.
- `DisconnectClient`: the client's stream is ended. `ClientDisconnected` fires and the connection is removed; `EventSource` then reconnects on its own with an empty queue. Use this when a client that missed events is better off resyncing from your API than silently skipping some.

Every event lost this way (including the one that triggers a disconnect) counts toward the "Dropped Deliveries" metric and the aggregated drop warning in the log. The queue holds events not yet written to the client; an event already being written when the stream ends is not counted as dropped.

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
| `LogEachDispatch` | `bool` | `false` | Also publish a live-log line for every event dispatched to a client. Connect and disconnect lines are always shown while a dashboard is open. Live logs and metric pushes are skipped entirely when the dashboard is disabled or no dashboard feed is connected. |
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

These are fixed in the current version: the query parameter used for event filtering (`eventType`), and the SSE event id scheme. See [Limitations](limitations.md).
