# JavaScript client

The stream is standard Server-Sent Events, so the browser's built-in [`EventSource`](https://developer.mozilla.org/docs/Web/API/EventSource) works as-is. PingDrop also ships an optional, dependency-free wrapper that adds typed handlers, event filtering and reconnect with backoff (see below).

## The bundled client

The library serves a small client script at `<EndpointPath>/client.js` (default `/pingdrop/stream/client.js`, `Content-Type: text/javascript; charset=utf-8`, `Cache-Control: public, max-age=3600`). The same file lives in the repository at [`clients/js`](../clients/js) (with typings, tests and an npm `package.json`, `pingdrop-client`). Set `PingDropOptions.ServeJavaScriptClient = false` to stop serving it.

```html
<script src="/pingdrop/stream/client.js"></script>
<script>
  const client = new PingDropClient({
    url: "/pingdrop/stream",
    eventTypes: ["notification", "alert"],   // server-side filter, sent as ?eventType=notification,alert
    params: { uId: "42" },                    // extra query parameters (e.g. your TargetKeyResolver key)
    withCredentials: true,
    reconnect: { initialDelayMs: 1000, maxDelayMs: 30000, factor: 2, jitter: 0.5 },
  });

  client.on("notification", (data, evt) => showToast(data.Text));  // data is JSON.parse'd, or the raw string
  client.on("open", refreshStateFromApi);
  client.on("error", () => console.warn(client.state));
  client.on("close", () => {});
  client.off("notification");
  client.close();
</script>
```

Behaviour: while the browser is retrying natively the client only reports `error`; when the browser gives up (readyState `CLOSED`: 401, 403, 204, wrong content type) the client creates a new `EventSource` after an exponentially growing, jittered delay, resetting on `open`. `close()` stops everything. On a manual reconnect it sends the last seen event id so the server can replay missed events (see below). Full reference: [`clients/js/README.md`](../clients/js/README.md).

The rest of this page describes the plain `EventSource` behaviour the client builds on.

## Basic usage

```js
const es = new EventSource("/pingdrop/stream");

es.addEventListener("notification", e => {
  const data = JSON.parse(e.data);
  console.log(e.lastEventId, data);
});

es.onerror = () => console.warn("PingDrop stream interrupted, readyState =", es.readyState);
```

The path is `PingDropOptions.EndpointPath` (default `/pingdrop/stream`).

## Event names

The `eventType` you pass on the server becomes the SSE `event:` field:

```csharp
await pingDrop.BroadcastAsync(new { Text = "Hi" }, "notification");   // event: notification
await pingDrop.BroadcastAsync(new { Text = "Hi" });                    // event: message (the default)
```

`EventSource` dispatches by event name:

- `eventType` `"message"` (the default): handled by `es.onmessage` or `es.addEventListener("message", ...)`.
- Any other name: handled **only** by `es.addEventListener("<name>", ...)`. `onmessage` does not fire for named events. This is the most common "I get nothing" mistake.

Each event's `data` is a single line of JSON produced by `System.Text.Json` with default options (property names keep the casing of your C# type; `new { Text = "Hi" }` arrives as `{"Text":"Hi"}`). `e.lastEventId` is the server-assigned numeric event id.

## Filtering by event type

Clients can ask the server to deliver only some event types with the `eventType` query parameter, a comma-separated list:

```js
const es = new EventSource("/pingdrop/stream?eventType=notification,alert");
```

- Matching is case-insensitive, and whitespace around names is trimmed.
- Filtering happens on the server, before the event is queued for that client, so filtered-out events cost the client nothing.
- If the parameter is missing or empty, the client receives every event type.
- It applies to the connection for its lifetime. To change the filter, close the `EventSource` and open a new one.

Filtering is not a security boundary; use authorization and per-user keys for that (see [Authentication](authentication.md)).

## Reconnect behaviour

`EventSource` reconnects automatically when the connection drops (server restart, network change, proxy timeout):

- The server suggests the delay with `retry: <ms>` at the start of each stream (`ClientRetryInterval`, default 3000 ms). The browser uses that value for subsequent reconnects of that `EventSource`.
- `es.readyState` is `CONNECTING (0)` while reconnecting, `OPEN (1)` while connected and `CLOSED (2)` after `es.close()` or a fatal error.
- Fatal errors are responses that are not `200` with `Content-Type: text/event-stream` (for example 401, 403, 404, a redirect to an HTML login page). The browser does not retry these. Handle them by checking `readyState`:

```js
es.onerror = () => {
  if (es.readyState === EventSource.CLOSED) {
    // fatal: authentication failed, wrong path, etc. Decide whether to re-authenticate and open a new EventSource.
  }
  // otherwise the browser is already retrying
};
```

### Replay of missed events (Last-Event-ID)

Every event is sent with an `id:` field. When a connection drops and the browser reconnects it, it sends a `Last-Event-ID` header with the last id it saw, and PingDrop replays the events it still has in its in-memory history with a larger id, before live events resume. Replay only includes what the client would have received live: broadcasts, events sent to its subscriber key, and its `eventType` filter. Turn it off with `EnableReplay = false` ([Configuration](configuration.md#enablereplay)).

If the browser gives up (`readyState === CLOSED`) and you create a new `EventSource`, it starts with no id and cannot set headers. Send the last id yourself as a query parameter, which the server accepts as a fallback:

```js
let lastId = null;
es.addEventListener('notification', e => { lastId = e.lastEventId; });
// later, after creating a new EventSource:
const es2 = new EventSource('/pingdrop/stream?uId=42' + (lastId ? '&lastEventId=' + lastId : ''));
```

`PingDropClient` does this for you (`client.lastEventId`, `replay: false` to disable).

Replay is best effort, not a delivery guarantee:

- It is limited to the last `EventHistorySize` events (default 50), kept in memory. A longer gap replays only what is retained.
- Event ids are per process and restart from 1 when the app restarts. If the id you send is greater than any id the server has issued (typical right after a restart), nothing is replayed, because the server cannot know what you missed. A stale id smaller than the new process's newest id replays events that are new to you, so do not treat ids as durable cursors.
- Events are tracked by the client only when a handler is registered for their type.

If you need reliable delivery, still treat PingDrop as a "wake-up" signal and re-fetch state from your API after every (re)connect:

```js
es.onopen = () => refreshStateFromApi();   // runs on first connect and after every reconnect
```

## Closing the connection

```js
window.addEventListener("pagehide", () => es.close());
```

Browsers close the stream when the page unloads, but calling `close()` explicitly is cleaner, and it is required when you replace an `EventSource` (for example after changing the filter or refreshing a token). A closed stream is detected by the server when the next write fails or the request is aborted; the dashboard then shows the connection as gone.

## Authentication from the browser

`EventSource` sends cookies on same-origin requests but cannot set custom headers. Cookie auth works unchanged; for bearer tokens see [Authentication](authentication.md#jwt-bearer-authentication). For cross-origin use see below.

### Cross-origin use

Configure CORS on the PingDrop app (`PingDropOptions.AllowedOrigins` or `CorsPolicy`, plus `app.UseCors()`; see [Configuration](configuration.md#cross-origin-use-cors)), then connect with an absolute URL:

```js
const es = new EventSource("https://api.example.com/pingdrop/stream", { withCredentials: true });
// or: new PingDropClient({ url: "https://api.example.com/pingdrop/stream", withCredentials: true })
```

- `withCredentials: true` is needed for cookies to be sent cross-origin. The server must then answer with `Access-Control-Allow-Credentials: true` and your exact origin (never `*`); `AllowedOrigins` does this.
- Cookies must also be sent cross-site: set `SameSite=None; Secure` on the auth cookie if the two origins are different sites.
- The first request is a simple GET and needs no preflight. When the browser reconnects it adds a `Last-Event-ID` header, which triggers a preflight; the CORS policy must allow that header (the built-in `AllowedOrigins` policy does), otherwise reconnects and replay fail with a CORS error.
- The bundled script can be loaded from the API origin with a plain `<script src="https://api.example.com/pingdrop/stream/client.js">` (script tags need no CORS); the CORS policy also covers `client.js` for `fetch` or `type="module"` loads.

## Connection limits

Over HTTP/1.1 browsers allow about six connections per origin, shared across all tabs. Each open `EventSource` holds one. Use HTTP/2 if users commonly open many tabs, or share one stream between tabs (for example with a `BroadcastChannel` or a `SharedWorker`).

## Handling messages robustly

```js
function connect() {
  const es = new EventSource("/pingdrop/stream?eventType=notification");

  es.addEventListener("notification", e => {
    let payload;
    try { payload = JSON.parse(e.data); } catch { return; }
    showToast(payload.Text);
  });

  es.onopen = refreshStateFromApi;
  es.onerror = () => { if (es.readyState === EventSource.CLOSED) setTimeout(connect, 5000); };
  return es;
}
```

(`showToast` and `refreshStateFromApi` are your own functions.)

## Without `EventSource`

Because the protocol is plain SSE over HTTP, you can consume the stream from any HTTP client that streams the response body, for example `fetch` with a `ReadableStream` reader (which allows an `Authorization` header), or `curl -N`. You then have to parse the `id:`, `event:` and `data:` lines and handle reconnects yourself.
