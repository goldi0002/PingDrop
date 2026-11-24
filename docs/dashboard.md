# Dashboard

PingDrop includes a built-in monitoring page. It is embedded in the library assembly (no static files or extra packages) and is served by `MapPingDrop()` when `EnableDashboard` is `true` (the default).

It shows:

- Counters: connected clients, events published, queued/attempted/successful/dropped deliveries and a success rate (all "since restart")
- The list of connections, with subscriber key, subscribed event types, events delivered and idle time, filterable by status and key
- A live log of connect/disconnect/dispatch activity
- Process information (memory, uptime, framework, OS)
- A form for sending test notifications to a key, a connection or everyone

All counters and history are in memory and reset when the process restarts.

## Where it lives

The dashboard path is `<EndpointPath>/dashboard`. With the defaults:

| Route | Method | Purpose |
|---|---|---|
| `/pingdrop/stream/dashboard` | GET | The HTML page |
| `/pingdrop/stream/dashboard/app.js` | GET | Page script |
| `/pingdrop/stream/dashboard/app.css` | GET | Page styles |
| `/pingdrop/stream/dashboard/metrics` | GET | Metrics (JSON) |
| `/pingdrop/stream/dashboard/about` | GET | Version and process info (JSON) |
| `/pingdrop/stream/dashboard/connections` | GET | Connection list (JSON); optional `q` parameter |
| `/pingdrop/stream/dashboard/send-notification` | POST | Send a notification (JSON body) |
| `/pingdrop/stream/dashboard/sse` | GET | The dashboard's own live feed (SSE) |

If you change `EndpointPath`, the dashboard moves with it. The dashboard title is `Dashboard.Title`.

## Security

The dashboard can push arbitrary payloads to any connected user and shows all subscriber keys. By default it is therefore only mapped in the Development environment. In other environments you must set `Dashboard.AuthorizationPolicy` (recommended) or `Dashboard.AllowAnonymous = true`; otherwise it is not mapped and a warning is logged at startup. One policy covers every route in the table above. Full details and examples are in [Authentication](authentication.md#securing-the-dashboard).

To remove it entirely, set `EnableDashboard = false`.

## Live log

The dashboard's log view shows client connect and disconnect lines. Per-dispatch lines ("Event dispatched to client ...") are off by default because they are noisy; set `Dashboard.LogEachDispatch = true` to include them. Live-log lines and metric pushes are only built when the dashboard is enabled and a dashboard feed is connected, so an unwatched dashboard adds no cost to delivery.

The dashboard's own live feed connects with a reserved subscriber key (`__pingdrop_dashboard__`). Sending a notification to that key through the dashboard API is rejected with `403`. The dashboard connection counts as a connection in the connection store, and broadcasts are also delivered to it.

## Sending from the dashboard

The page's send form calls `POST <dashboard>/send-notification` with a JSON body. You can call it yourself (subject to the same authorization):

```json
{
  "targetType": "key",
  "targetId": "user-42",
  "eventName": "notification",
  "payload": { "Text": "Hello from the dashboard" }
}
```

| Field | Required | Description |
|---|---|---|
| `targetType` | yes | `key`, `connection` or `broadcast` (case-insensitive) |
| `targetId` | for `key` and `connection` | The subscriber key, or a connection id as shown in the connections list |
| `eventName` | yes | The SSE event name (what `addEventListener` listens for) |
| `payload` | yes | Any JSON value; it is re-serialized and sent as the event `data` |
| `timestamp` | no | Accepted but not used |

Target types:

- `broadcast` sends to every connected client (same as `BroadcastAsync`).
- `key` sends to every connection with that subscriber key (same as `SendToAsync`).
- `connection` looks up the connection by id and then sends to **its subscriber key**. All connections that share that key receive the event, not just the one you picked, and a connection without a key cannot be targeted this way (404).

Responses use the envelope `{ "isSuccess": bool, "message": string?, "data": ..., "errors": [...]? }`:

| Status | When |
|---|---|
| 200 | Sent. `message` is `Notification sent successfully` |
| 400 | Missing/invalid body, unknown `targetType`, missing `eventName` or `payload`, or `key` without `targetId` |
| 403 | `targetId` is the dashboard's reserved key |
| 404 | `connection` id not found, or the connection has no key |
| 413 | The serialized payload is larger than `MaxPayloadBytes`; nothing was sent |
| 429 | More than `Dashboard.MaxSendsPerMinute` requests in the current minute; see `Retry-After` |

A 200 means the request was accepted and queued for matching connections. A `key` send to a key with no connections also returns 200, because the underlying `SendToAsync` does nothing for unknown keys.

Example with `curl` (add credentials if the dashboard is secured):

```
curl -X POST http://localhost:5000/pingdrop/stream/dashboard/send-notification \
  -H "Content-Type: application/json" \
  -d '{"targetType":"broadcast","eventName":"notification","payload":{"Text":"Hi"}}'
```

## Limits

- `Dashboard.MaxSendsPerMinute` (default `null`, unlimited) caps `POST <dashboard>/send-notification`. It is a small in-memory fixed-window limiter inside the library (no ASP.NET Core rate-limiting setup needed): the window starts with the first request and lasts one minute, the count is shared by every caller of this process, and every request counts, including ones that are later rejected as invalid. A request over the limit gets `429`, a `Retry-After` header (seconds until the window resets) and the usual failure envelope. A fixed window allows up to twice the limit across a window boundary. Sends from your own code through `IPingDropService` are not affected. The limiter's clock is the `TimeProvider` registered in DI (`TimeProvider.System` otherwise).
- `Dashboard.MaxFeedConnections` (default `10`) caps open dashboard pages (live feeds). A feed over the cap gets `503` with `Retry-After`; the page still loads but its live updates do not connect. Feeds are not counted in `MaxConnectionsPerKey`/`MaxTotalConnections`.
- `MaxPayloadBytes` applies to dashboard sends too and returns `413`.

## Reading the metrics

| Metric | Meaning |
|---|---|
| Connected Clients | Open SSE connections, including the dashboard's own feed |
| Events Published | Number of `BroadcastAsync`/`SendToAsync` calls that had at least one connection to consider |
| Queued Notifications | Deliveries placed into client queues |
| Attempted Deliveries | Per-client delivery attempts after event-type filtering |
| Successful Deliveries | Events actually written to a client's response |
| Dropped Deliveries | Writes that failed or were cancelled while sending to a client |
| Delivery Success Rate | Successful / attempted |
| History Events | Entries currently held in the history ring (`EventHistorySize`) |

"Queued" and "Attempted" move together. Events discarded because a client's queue was full are not currently counted as dropped; see [Limitations](limitations.md#per-client-queue).
