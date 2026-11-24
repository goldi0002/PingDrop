# Limitations

PingDrop is deliberately small. This page lists what it does not do, so you can decide whether it fits.

## Single node, in-memory

All state lives in the memory of one process: the connection table, the per-client queues, the event history and every counter. There is no backplane and no shared store.

- A notification sent on instance A reaches only the clients connected to instance A. With two or more instances behind a load balancer, `BroadcastAsync` reaches a fraction of your users and `SendToAsync` reaches the user only if their stream happens to be on the instance that handled the call. Sticky sessions do not fix this: the sender and the recipient are usually on different instances.
- If you scale out, you have to route sends yourself (for example, publish through your own queue or pub/sub and have every instance call `IPingDropService` locally). PingDrop does not do this for you.
- Restarting or redeploying the process drops every connection and resets all metrics. Clients reconnect on their own (see [JavaScript client](javascript-client.md)), but they start with nothing.

## No replay, no durability

- Events are not stored for delivery. If a client is disconnected when an event is sent, it never receives it.
- The `Last-Event-ID` header sent by reconnecting browsers is ignored. See [JavaScript client](javascript-client.md#events-are-not-replayed-last-event-id-is-ignored).
- There is no acknowledgement. `BroadcastAsync` and `SendToAsync` return when the event has been placed in each client's queue, not when it has been written to the network or received by the browser.
- Event ids are per-process counters that restart at 1.

Use PingDrop for transient notifications, and keep the source of truth in your database or API.

## Per-client queue

Each connection has its own bounded queue, **20** notifications by default with a **drop-oldest** policy. If a client reads slowly (slow network, throttled background tab) and the queue fills, the oldest unsent events are discarded to make room for new ones. Both are configurable: `ClientQueueCapacity` sets the size, and `ClientQueueFullMode` chooses `DropOldest`, `DropNewest` or `DisconnectClient` (end the stream so the client reconnects). See [Configuration](configuration.md#clientqueuecapacity--clientqueuefullmode).

Delivery order within one client is preserved for the events that remain. Events discarded by overflow are counted in the dashboard's "Dropped Deliveries" metric. Nothing replays them, so a client that was dropped from or disconnected will not receive the lost events (see "No replay" above).

## Event history

`EventHistorySize` (default 50) controls an in-memory ring of recent events. It exists for the dashboard's benefit. It is not used for replay, it is not queryable through the public API for your own use, and it is lost on restart.

## Delivery model

- One-way: server to browser. Clients cannot send messages over the stream; use ordinary HTTP requests for that.
- Event-type filtering is by exact name (case-insensitive) and fixed at connect time.
- Targeting is by one subscriber key per connection, set when the connection opens. A connection can hold exactly one key (or none). There is no group or topic abstraction beyond that.
- Payloads are serialized once per send with `System.Text.Json` default options. PingDrop enforces no size limit, but each event is written separately to every recipient's stream, so keep payloads small.
- Authorization is checked when the connection opens, not for every event. A stream stays open after a user's session or token expires. See [Authentication](authentication.md#authorization-and-long-lived-connections).
- There is no API to disconnect a specific client or to list connections other than through the dashboard.

## Browser and network limits

- `EventSource` cannot set request headers, which affects bearer-token authentication. See [Authentication](authentication.md).
- Over HTTP/1.1 browsers allow about six connections per origin, shared by all tabs.
- Proxies can buffer or time out streams. See [Reverse proxy](reverse-proxy.md).

## Dashboard

- It reads only the local process, so on multiple instances it shows whichever instance served the request.
- Metrics and the connection list are snapshots from memory; they are not persisted or exported to a metrics system. The library emits `System.Diagnostics.Activity` spans from an `ActivitySource` named `PingDrop` for sends, which you can subscribe to with OpenTelemetry.
