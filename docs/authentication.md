# Authentication

PingDrop does not implement authentication. It plugs into ASP.NET Core's authorization: you give it the name of a policy, and it calls `RequireAuthorization(policy)` on the endpoints it maps. Who the user is comes from whatever authentication handler your app already uses.

There are two separate things to secure, and they are configured separately:

| Surface | Option | Default |
|---|---|---|
| SSE endpoint (`/pingdrop/stream`) | `PingDropOptions.AuthorizationPolicy` | `null`: anonymous |
| Dashboard (`/pingdrop/stream/dashboard/...`) | `PingDropOptions.Dashboard.AuthorizationPolicy` | `null`: Development only |

## Pipeline requirements

Define the policy with `AddAuthorization`, add the authentication scheme(s), and call `UseAuthentication()` and `UseAuthorization()` before `MapPingDrop()`:

```csharp
var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapPingDrop();
```

PingDrop only attaches the policy name to the endpoints. If the policy does not exist, requests to the endpoint fail at runtime.

## Do not use the query string as the identity

The test app in this repository uses `ctx.Request.Query["uId"]` as the subscriber key because it is convenient. Do not do that in production. The `EventSource` URL is chosen by the client, so any client could connect as `?uId=someone-else` and receive that person's notifications. Derive the key from the authenticated principal instead, and require authorization on the endpoint so `ctx.User` is populated.

```csharp
builder.Services.AddPingDrop(o =>
{
    o.AuthorizationPolicy = "PingDropClients";
    o.TargetKeyResolver = ctx => ctx.User.FindFirst("sub")?.Value;
    // or ClaimTypes.NameIdentifier, an email claim, a tenant-scoped id, ...
});
```

`TargetKeyResolver` runs after authorization for the endpoint, so for an authorized request `ctx.User` is the authenticated user. Returning `null` leaves the connection keyless (it still receives broadcasts).

Then send from server code using the same value:

```csharp
await pingDrop.SendToAsync(userId, new { Text = "Your export is ready" }, "notification");
```

If your key combines claims (for example tenant and user), build the same string on both sides, e.g. `$"{tenantId}:{userId}"`.

## Cookie authentication

Cookies are the simplest option for browsers. `EventSource` sends cookies automatically for same-origin requests, so no client changes are needed.

```csharp
using Microsoft.AspNetCore.Authentication.Cookies;
using PingDrop.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        // EventSource cannot follow a login redirect, so answer with a status code.
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
    });

builder.Services.AddAuthorization(a =>
{
    a.AddPolicy("PingDropClients", p => p.RequireAuthenticatedUser());
    a.AddPolicy("PingDropAdmins", p => p.RequireRole("admin"));
});

builder.Services.AddPingDrop(o =>
{
    o.AuthorizationPolicy = "PingDropClients";
    o.TargetKeyResolver = ctx => ctx.User.FindFirst("sub")?.Value;
    o.Dashboard.AuthorizationPolicy = "PingDropAdmins";
});

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapPingDrop();
app.Run();
```

Overriding the redirect events is optional but gives clearer failures: by default the cookie handler answers an unauthenticated request with a redirect to a login page, which `EventSource` cannot use (the response is not `text/event-stream`), so the connection fails. Browsers treat any response that is not `200 text/event-stream`, including `401` and `403`, as fatal: the `EventSource` moves to the closed state and does not retry on its own. See [JavaScript client](javascript-client.md).

If the page and the PingDrop endpoint are on different origins, you also need CORS and `new EventSource(url, { withCredentials: true })`, plus `SameSite=None; Secure` cookies. Serving both from the same origin avoids all of that.

## JWT bearer authentication

The browser `EventSource` API cannot set an `Authorization` header. There are two practical options.

**Option 1: send the token in the query string.** Configure the JWT handler to read it for the PingDrop path only:

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        // o.Authority / o.TokenValidationParameters as for the rest of your API
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                if (ctx.HttpContext.Request.Path.StartsWithSegments("/pingdrop/stream") &&
                    ctx.Request.Query.TryGetValue("access_token", out var token))
                {
                    ctx.Token = token;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(a =>
{
    a.AddPolicy("PingDropClients", p => p.RequireAuthenticatedUser());
    a.AddPolicy("PingDropAdmins", p => p.RequireRole("admin"));
});

builder.Services.AddPingDrop(o =>
{
    o.AuthorizationPolicy = "PingDropClients";
    o.TargetKeyResolver = ctx => ctx.User.FindFirst("sub")?.Value;
    o.Dashboard.AuthorizationPolicy = "PingDropAdmins";
});
```

```js
const es = new EventSource("/pingdrop/stream?access_token=" + encodeURIComponent(jwt));
```

Trade-offs: URLs end up in reverse-proxy access logs, browser history and monitoring tools, so use short-lived tokens, strip the query string from your logs, and prefer a dedicated short-lived token over your main access token. The token is also fixed for the life of the connection; when it expires you must close the `EventSource` and open a new one with a fresh token. Automatic browser reconnects reuse the original URL, including the old token, so if the connection drops after the token expired, the reconnect gets a 401 and the `EventSource` closes for good. Handle `onerror` by checking `es.readyState === EventSource.CLOSED` and opening a new connection with a fresh token.

The path check above matches `StartsWithSegments("/pingdrop/stream")`, which also covers the dashboard routes. If you do not want the dashboard to accept query-string tokens, compare against the exact path instead.

**Option 2: exchange for a cookie.** Let the user sign in with a cookie (previous section) and use JWT for your regular API calls only.

If a JWT scheme is not your default scheme, name it on the policy: `p.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser()`.

## Securing the dashboard

The dashboard can send arbitrary notifications to any connected client and lists every connection with its subscriber key. Treat it as an admin tool.

```csharp
builder.Services.AddAuthorization(a =>
    a.AddPolicy("PingDropAdmins", p => p.RequireRole("admin")));

builder.Services.AddPingDrop(o =>
{
    o.Dashboard.AuthorizationPolicy = "PingDropAdmins";
});
```

The policy is applied to one route group that contains every dashboard route: the HTML page, `app.js`, `app.css`, `metrics`, `about`, `connections`, `send-notification` and the live feed `sse`. There is no route that is exempt.

### Development-only default

If `Dashboard.AuthorizationPolicy` is not set, the dashboard behaves as follows:

| Environment | `Dashboard.AllowAnonymous` | Result |
|---|---|---|
| Development | any | Mapped, anonymous |
| Anything else | `false` (default) | Not mapped (requests return 404) and a warning is logged |
| Anything else | `true` | Mapped, anonymous |

The warning looks like this:

```
The PingDrop dashboard is enabled but was not mapped: it has no authorization policy and the environment is 'Production'. Set PingDropOptions.Dashboard.AuthorizationPolicy (recommended), set Dashboard.AllowAnonymous = true, or set EnableDashboard = false.
```

If you see it in production, that is the library refusing to expose an unauthenticated admin surface. Fix it with a policy. Use `AllowAnonymous = true` only when something else already restricts access, for example a private network or an authenticating reverse proxy.

If you do not want a dashboard at all, set `EnableDashboard = false`.

## Authorization and long-lived connections

Authorization is evaluated once, when the SSE request starts. If a user's session is revoked or a token expires, an already-open stream keeps running until it disconnects. If that matters, close streams from your side by limiting session lifetime (for example, have the client reconnect periodically) and note that PingDrop currently has no API to disconnect a client by key.
