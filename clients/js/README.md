# pingdrop-client

A tiny (~3 KB), dependency-free wrapper around the browser `EventSource` for [PingDrop](https://github.com/goldi0002/PingDrop) streams: typed JSON handlers, server-side event type filtering and exponential backoff with jitter.

## Get it

- From your PingDrop app (enabled by default): `<script src="/pingdrop/stream/client.js"></script>` (path is `{EndpointPath}/client.js`; disable with `PingDropOptions.ServeJavaScriptClient = false`).
- Or copy `pingdrop.js` / `npm install` this folder. It is UMD: a global `PingDropClient`, `require()`, or `import PingDropClient from './pingdrop.js'` through a bundler. Typings are in `pingdrop.d.ts`.

## Usage

```js
const client = new PingDropClient({
  url: '/pingdrop/stream',
  eventTypes: ['notification', 'alert'],   // sent as ?eventType=notification,alert
  params: { uId: '42' },                    // extra query parameters
  withCredentials: true,
  reconnect: { initialDelayMs: 1000, maxDelayMs: 30000, factor: 2, jitter: 0.5 },
});

client.on('notification', (data, evt) => console.log(data, evt.lastEventId));
client.on('open', () => refreshStateFromApi());
client.on('error', () => console.warn(client.state));
client.on('close', () => console.log('closed'));

client.off('notification');   // or client.off('notification', handler)
client.close();               // stops for good, no further reconnects
```

`data` is `JSON.parse`d; if the payload is not valid JSON you get the raw string. `client.state` is `'connecting'`, `'open'` or `'closed'`.

## Reconnects

While the browser is retrying by itself (`readyState` CONNECTING) the client just reports `error`. When the browser gives up (`readyState` CLOSED, e.g. HTTP 401/403/204 or a wrong content type) the client opens a new `EventSource` after `min(maxDelayMs, initialDelayMs * factor^attempt)`, reduced by up to `jitter` (a 0 to 1 fraction) at random. The attempt counter resets on `open`. Pass `reconnect: false` to disable this; the client then emits `close`.

### Missed events (replay)

The server replays retained events a client missed. When the browser reconnects an open stream it sends a `Last-Event-ID` header by itself. When the browser gave up and this client creates a new `EventSource`, the new object has no id, so the client tracks the last `evt.lastEventId` it delivered (also exposed as `client.lastEventId`) and connects with `?lastEventId=N`, which the server accepts as a fallback to the header. Pass `replay: false` to turn this off, or set `params.lastEventId` yourself to take control.

Replay is best effort: it is limited to the server's `EventHistorySize`, held in memory, and ids restart when the server process restarts (a larger id than the server has issued replays nothing). Only events that have a handler registered are tracked. Keep re-fetching state on `open` if you need certainty.

## Options

`url`, `eventTypes`, `params`, `withCredentials`, `reconnect`, `replay` (default true), `autoConnect` (default true; call `client.connect()` yourself when false), and for tests `EventSourceImpl`, `random`, `setTimeoutImpl`, `clearTimeoutImpl`.

## Tests

`node --test test/*.test.js` (Node 18+, no dependencies).
