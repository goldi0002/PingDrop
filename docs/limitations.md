# Limitations

PingDrop is deliberately small. This page lists what it does not do, so you can decide whether it fits.

## Single node, in-memory

All state lives in the memory of one process: the connection table, the per-client queues, the event history and every counter. There is no backplane and no shared store.

- A notification sent on instance A reaches only the clients connected to instance A. With two or more instances behind a load balancer, `BroadcastAsync` reaches a fraction of your users and `SendToAsync` reaches the user only if their stream happens to be on the instance that handled the call. Sticky sessions do not fix this: the sender and the recipient are usually on different instances.
- If you scale out, you have to route sends yourself (for example, publish through your own queue or pub/sub and have every instance call `IPingDropService` locally). PingDrop does not do this for you.
- Restarting or redeploying the process drops every connection and resets all metrics. Clients reconnect on their own (see [JavaScript client](javascript-client.md)), but they start with nothing.

## Best-effort replay, no durability

- Replay is best effort. A client that reconnects with `Last-Event-ID` (or `?lastEventId=`) is sent the events it missed that are still in the in-memory history (`EventHistorySize`, default 50), limited to what it would have received live. Events that fell out of the history, or were sent before the last restart, are gone. See [JavaScript client](javascript-client.md#replay-of-missed-events-last-event-id). Disable it with `EnableReplay = false`.
- Ids are per process. After a restart ids start again at 1, so the server cannot tell what a client missed: a `Last-Event-ID` larger than any id the new process has issued replays nothing, and a stale id that happens to be smaller than the new process's newest id replays events that are new to that client. Treat replay as a gap filler for short network drops, not as a durable log.
- Replay is not acknowledged and not guaranteed. Replay needs the client to send its last id, which a browser does only after it has received an event with an `id:` field; a client that never received one gets no replay.
- There is no acknowledgement. `BroadcastAsync` and `SendToAsync` return when the event has been placed in each client's queue, not when it has been written to the network or received by the browser.
- Event ids are per-process counters that restart at 1.

Use PingDrop for transient notifications, and keep the source of truth in your database or API.

## Per-client queue

Each connection has its own bounded queue, **20** notifications by default with a **drop-oldest** policy. If a client reads slowly (slow network, throttled background tab) and the queue fills, the oldest unsent events are discarded to make room for new ones. Both are configurable: `ClientQueueCapacity` sets the size, and `ClientQueueFullMode` chooses `DropOldest`, `DropNewest` or `DisconnectClient` (end the stream so the client reconnects). See [Configuration](configuration.md#clientqueuecapacity--clientqueuefullmode).

Delivery order within one client is preserved for the events that remain. Events discarded by overflow are counted in the dashboard's "Dropped Deliveries" metric. Events lost to overflow are not retried while the connection stays open; a client that is disconnected (for example under `DisconnectClient`) can recover the ones still in the history when it reconnects (see "Best-effort replay" above).

## Event history

`EventHistorySize` (default 50) controls an in-memory ring of recent events. It feeds the dashboard and is the source for `Last-Event-ID` replay, so it also bounds how far back a reconnecting client can catch up. It is not queryable through the public API for your own use, and it is lost on restart. With `EnableReplay` on (the default), an event is recorded even when no client is connected, so the only listener can catch up after a gap; such an event is not counted in "Events Published".

## Delivery model

- One-way: server to browser. Clients cannot send messages over the stream; use ordinary HTTP requests for that.
- Event-type filtering is by exact name (case-insensitive) and fixed at connect time.
- Targeting is by one subscriber key per connection, set when the connection opens. A connection can hold exactly one key (or none). There is no group or topic abstraction beyond that.
- Payloads are serialized once per send with `System.Text.Json` default options. There is no size limit unless you set `MaxPayloadBytes` (an oversize send then throws `PingDropPayloadTooLargeException`); each event is written separately to every recipient's stream, so keep payloads small.
- Authorization is checked when the connection opens, not for every event. A stream stays open after a user's session or token expires. See [Authentication](authentication.md#authorization-and-long-lived-connections).
- There is no API to disconnect a specific client or to list connections other than through the dashboard.

## Limits you can set

Everything below is unlimited by default and is enforced per process (with several instances each applies its own limit). See [Configuration](configuration.md#limits).

- `MaxConnectionsPerKey` (429) and `MaxTotalConnections` (503) refuse new SSE connections with a `Retry-After` header. Browsers' `EventSource` does not retry after a non-200 answer, so a bare `EventSource` page has to retry itself (the bundled JavaScript client retries with backoff). The dashboard's live feed is not counted; it has its own cap, `Dashboard.MaxFeedConnections` (default 10).
- `MaxPayloadBytes` makes oversize sends throw; the dashboard's send endpoint answers 413.
- `Dashboard.MaxSendsPerMinute` rate-limits the dashboard's send endpoint (429, fixed window, in memory). It does not limit your own `IPingDropService` calls, and PingDrop has no limit on how fast application code may publish.
- There is no per-IP limit and no limit on the size of a request to the dashboard's send endpoint beyond ASP.NET Core's own request limits.

## Browser and network limits

- `EventSource` cannot set request headers, which affects bearer-token authentication. See [Authentication](authentication.md).
- Over HTTP/1.1 browsers allow about six connections per origin, shared by all tabs.
- Proxies can buffer or time out streams. See [Reverse proxy](reverse-proxy.md).

## Dashboard

- It reads only the local process, so on multiple instances it shows whichever instance served the request.
- Metrics and the connection list are snapshots from memory; they are not persisted or exported to a metrics system. The library emits `System.Diagnostics.Activity` spans from an `ActivitySource` named `PingDrop` for sends, which you can subscribe to with OpenTelemetry.
