# Reverse proxies and hosting

An SSE stream is a normal HTTP response that never ends. Anything between the browser and Kestrel can break that in two ways:

1. **Buffering.** The proxy collects the response and forwards it in chunks (or at the end). Events then arrive late, in bursts, or never.
2. **Idle timeouts.** The proxy closes a connection that has carried no bytes for N seconds. The browser reconnects, but each reconnect is a gap. PingDrop replays retained events on reconnect (best effort, limited by `EventHistorySize`; see [Limitations](limitations.md)), so a short gap is usually recovered.

## What PingDrop sends

Every SSE response (the stream endpoint and the dashboard feed) carries these headers:

```
Content-Type: text/event-stream
Cache-Control: no-cache, no-transform
X-Accel-Buffering: no
Connection: keep-alive
```

- `X-Accel-Buffering: no` tells nginx (and compatible proxies) not to buffer this response, so for a default nginx setup streaming usually works without extra configuration. Other proxies ignore it.
- `Cache-Control: no-transform` asks intermediaries not to modify or compress the body.
- The first bytes written are an optional `retry: 3000` line and a `: connected` comment, flushed immediately so the browser sees headers right away.
- While the connection is idle, a `: ping` comment is written every `HeartbeatInterval` (default 15 seconds). It is an SSE comment: `EventSource` ignores it, but the bytes keep idle timers from firing.

## Heartbeat interval versus idle timeouts

Set `HeartbeatInterval` to well under the shortest idle timeout on the path (a third to a half is a reasonable margin). The default of 15 seconds fits most timeouts of 30 seconds or more.

| Component | Typical default idle/read timeout | Fits 15 s heartbeat? |
|---|---|---|
| nginx `proxy_read_timeout` | 60 s | Yes |
| Apache `ProxyTimeout` | 60 s (falls back to `Timeout`) | Yes |
| AWS ALB idle timeout | 60 s | Yes |
| Cloudflare (proxied) | about 100 s with no data | Yes |
| Azure App Service front end | about 230 s | Yes |
| IIS ARR `timeout` | 30 s | Yes, but with less margin |
| Azure Application Gateway request timeout | 20 s (v2 default, configurable) | Marginal: lower `HeartbeatInterval` to 5-10 s or raise the setting |

These numbers are the commonly documented defaults at the time of writing; they vary by plan and version, so confirm them for your environment. The principle holds regardless: heartbeat interval must be smaller than the smallest timeout.

If you cannot change a short timeout, lower the heartbeat:

```csharp
builder.Services.AddPingDrop(o => o.HeartbeatInterval = TimeSpan.FromSeconds(10));
```

Setting `HeartbeatInterval` to zero disables heartbeats entirely. Only do that if nothing between the browser and server has an idle timeout, and note that without heartbeats a dead peer is only noticed when a write fails.

## nginx

```nginx
location /pingdrop/stream {
    proxy_pass http://127.0.0.1:5000;

    proxy_http_version 1.1;
    proxy_set_header Connection "";

    proxy_buffering off;      # also set per-response by X-Accel-Buffering: no
    proxy_cache off;
    proxy_read_timeout 1h;    # must exceed the longest silence; heartbeats make this a safety net
    proxy_send_timeout 1h;

    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
}
```

Notes:

- `location /pingdrop/stream` matches the stream and also `/pingdrop/stream/dashboard/...` by prefix. If you changed `EndpointPath`, change the location to match.
- `proxy_http_version 1.1` with `Connection ""` enables keep-alive to the upstream; nginx's default is HTTP/1.0 to upstreams, which does not support this well.
- Do not enable `gzip` for `text/event-stream`. nginx's default `gzip_types` does not include it, so only worry if you broadened that list.
- With `proxy_read_timeout 60s` (the default) and the 15-second heartbeat the stream survives indefinitely; raising the timeout is optional.

## IIS and ARR (Application Request Routing)

When IIS terminates the request and forwards it with ARR (or URL Rewrite) to Kestrel:

- Open **Application Request Routing Cache > Server Proxy Settings**. Set **Response buffer threshold** to `0` so ARR streams immediately instead of buffering. The setting is stored as `responseBufferLimit="0"` on the `<proxy>` element in `applicationHost.config`.
- Raise the proxy **Time-out** above your heartbeat interval. The ARR default is 30 seconds, so the 15-second heartbeat fits but with little margin.
- Disable dynamic compression for `text/event-stream` (compression buffers output).

If you host ASP.NET Core in-process behind IIS (the ASP.NET Core Module, in-process), there is no proxy hop to Kestrel, but IIS dynamic compression and any front load balancer still apply.

## Azure

- **App Service (Linux or Windows).** The front end closes connections idle for about 230 seconds, which the heartbeat avoids. Enable **ARR Affinity** if you run more than one instance and need a client to reconnect to the same instance, but see [Limitations](limitations.md): events sent on one instance are not visible to clients on another, whatever affinity you use.
- **Application Gateway.** Increase the backend setting's **Request time-out** if it is below your heartbeat margin (default 20 s on v2). Verify streaming end to end with `curl -N` (see below).
- **Front Door / CDN.** Disable caching and compression for the stream path, and check that the service streams `text/event-stream` responses without buffering; behaviour differs by tier and configuration.

## Cloudflare

Cloudflare proxies `text/event-stream` responses through without caching them, but closes connections that send no data for about 100 seconds on standard plans (HTTP 524 for timeouts waiting on the origin). The 15-second heartbeat keeps the connection alive. Practical advice:

- Do not add a cache rule or Rocket Loader/minification features for the stream path.
- If events appear in bursts rather than instantly, check for a Cloudflare feature or Worker in front of the stream that buffers responses.
- Cloudflare Tunnel and other zero-trust products have their own idle settings; check them if disconnects happen at a regular interval.

## Other proxies and load balancers

- **AWS ALB:** idle timeout defaults to 60 s; the heartbeat covers that. Sticky sessions do not give cross-instance delivery.
- **Traefik, HAProxy, Envoy:** disable response buffering for the path if the proxy has such a feature, and set idle/stream timeouts above the heartbeat interval (`timeout tunnel`/`timeout server` in HAProxy, stream idle timeout in Envoy).
- **Kubernetes ingress-nginx:** use the annotations `nginx.ingress.kubernetes.io/proxy-buffering: "off"` and `nginx.ingress.kubernetes.io/proxy-read-timeout: "3600"`.

## Hosting under a path prefix

If the app is reachable under a prefix (`https://example.com/app/...`), routes are still mapped relative to the app, and the prefix is the app's `PathBase`.

- Either the proxy **strips** the prefix and the app puts it back with `app.UsePathBase("/app")` (call it before `UseRouting`, and call `UseRouting` explicitly after it), or the proxy forwards the prefix and the app's own routing handles it with `UsePathBase`. Either way, PingDrop reads `Request.PathBase`.
- `X-Forwarded-Prefix` is a convention of some proxies (Traefik, Spring, Istio) and is not applied by ASP.NET Core's `ForwardedHeaders` middleware, which only handles `For`, `Proto` and `Host`. To honour it, set the path base from the header yourself, for example `app.Use((ctx, next) => { if (ctx.Request.Headers.TryGetValue("X-Forwarded-Prefix", out var p) && p.Count > 0) ctx.Request.PathBase = new PathString(p.ToString().TrimEnd('/')); return next(); });` before `UseRouting`, and only accept it from a trusted proxy.
- The dashboard page is rendered with the path base in all its URLs (script, stylesheet, API calls and its live feed), so it works under the prefix. Pages that use the JavaScript client pass the full URL they want to connect to, for example `new PingDropClient({ url: "/app/pingdrop/stream" })`; the bundled client contains no absolute URLs of its own.
- In nginx, match the location on the prefix, for example `location /app/pingdrop/stream` (see above), and keep the buffering and timeout settings.

## Rolling restarts and graceful shutdown

When the host starts stopping (`ApplicationStopping`, for example on SIGTERM or a deployment), PingDrop completes every open stream, the dashboard feed included. Each client's stream ends cleanly, `ClientDisconnected` runs for it, and the host does not wait out the shutdown timeout (30 s by default) for streams that would otherwise stay open. Browsers' `EventSource` then reconnects after `ClientRetryInterval`, which lets the load balancer send them to the next instance.

A stream request that arrives after shutdown has begun is answered with `503 Service Unavailable` (`EventSource` retries it). Notifications still queued for a client at that moment are flushed before its stream ends only if the host's shutdown timeout allows; ids restart with the process, so a client cannot replay across a restart and can miss events during one (see [Limitations](limitations.md)).

## HTTP/1.1 connection limits

Browsers allow only about six HTTP/1.1 connections per origin. Each tab with an open `EventSource` uses one for the lifetime of the page, so a user with many tabs can starve normal requests. Serving over HTTP/2 (which multiplexes streams over one connection) avoids this; most TLS-terminating proxies and Kestrel with HTTPS support it.

## Verifying

With the app running, `curl -N` shows events and heartbeats as they happen:

```
curl -N http://localhost:5000/pingdrop/stream
```

You should see `: connected` immediately and `: ping` every heartbeat interval. Run the same command through your proxy. If the output arrives in bursts, or `: connected` does not appear until later, something is buffering.
