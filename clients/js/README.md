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

Note that PingDrop does not replay missed events yet (`Last-Event-ID` is ignored by the server), so re-fetch state on every `open`.

## Options

`url`, `eventTypes`, `params`, `withCredentials`, `reconnect`, `autoConnect` (default true; call `client.connect()` yourself when false), and for tests `EventSourceImpl`, `random`, `setTimeoutImpl`, `clearTimeoutImpl`.

## Tests

`node --test test/*.test.js` (Node 18+, no dependencies).
