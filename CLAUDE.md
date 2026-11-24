# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

PingDrop is a lightweight real-time notification library for ASP.NET Core (README: "Lightweight real-time notifications for ASP.NET Core"). It pushes events to browsers over Server-Sent Events (SSE) and ships a built-in monitoring dashboard. It targets `net8.0` and is packaged as a NuGet package (`PingDrop`, v0.9.0, MIT).

The solution (`PingDrop.sln`) has two projects:
- `PingDrop/` is the library, a class library with `FrameworkReference Microsoft.AspNetCore.App`.
- `PingDrop.Web.Test/` is a Razor Pages host app that references the library. It is a manual test harness, not an automated test suite.

There are no automated tests, no linter config and no CI in the repo.

## Commands

The `dotnet` SDK was not installed in the cloud container when this file was written, so these commands are untested there.

```
dotnet build PingDrop.sln
dotnet run --project PingDrop.Web.Test      # launch profiles are in Properties/launchSettings.json
dotnet pack PingDrop/PingDrop.csproj
```

`GeneratePackageOnBuild` is true, so every build of the library also produces a `.nupkg`. `GenerateDocumentationFile` is also on, so XML doc comments are expected on public members.

`PingDrop.csproj` packs `icon.png` and `README.md` from the `PingDrop/` folder, and neither is tracked there. If a build or pack complains about them, that is the cause.

## Architecture

**Public surface** (`Abstractions/`, `Extensions/`):
- `services.AddPingDrop(opts => ...)` in `ServiceCollectionExtensions` registers everything as singletons.
- `app.MapPingDrop()` in `EndpointRouteBuilderExtensions` maps the SSE endpoint (`PingDropOptions.EndpointPath`, default `/pingdrop/stream`). If `EnableDashboard` is true, it also maps the dashboard routes under `Dashboard.Path`.
- Application code sends notifications through `IPingDropService`, using `BroadcastAsync` and `SendToAsync(targetKey, ...)`.
- `PingDropOptions` holds the `ClientConnected` and `ClientDisconnected` callbacks, `EventHistorySize` (default 50), and `TargetKeyResolver`. `TargetKeyResolver` maps an incoming SSE request to a "subscriber key", for example `context.Request.Query["uId"]` in the test app.

**Core flow** (`Internal/`):
- `PingDropHub` is the central singleton. It is registered as the concrete type and aliased as `IPingDropService`. It resolves the target connections, builds a `PDNotification` through `INotificationFactory`, records it in `IEventHistoryStore`, and fans it out to the matching `PDClientConnection`s. It also keeps the delivery counters (published, attempted, successful, dropped) with `Interlocked`, and emits `Activity` spans through an `ActivitySource`.
- `PDConnectionStore` indexes connections by connection id and by subscriber key, which is how `SendToAsync(key, ...)` finds its targets.
- `PDClientConnection` is one SSE client. It writes through `PDSseWriter`, and a client can restrict itself to certain event types (`HasSubscriptions` / `IsSubscribedTo`).
- `PDEventHistoryStore` is a bounded ring of recent events.

**Dashboard** (`Dashboard/` and `DashboardService`):
- `Index.html`, `app.css` and `app.js` are compiled in as `EmbeddedResource` and read at runtime through `PDEmbeddedFileProvider.ReadAsync`. The resource name is `PingDrop.Dashboard.<file>`, so keep new dashboard assets in that folder and namespace.
- `Index.html` uses `{{DASHBOARD_PATH}}` and `{{DASHBOARD_TITLE}}` placeholders. `MapPingDrop` replaces them when serving the page.
- The dashboard has its own SSE feed (`<DashboardPath>/sse`), which `HandleDashboardSseConnection` subscribes under the reserved key `InternalConstants.DashboardSubscriberKey`. The dashboard's feed is driven by `PDEventPublisher` (`IEventPublisher`) pushing `"metrics"` events.
- The dashboard's REST endpoints (`/metrics`, `/about`, `/connections`, `POST /send-notification`) live in `AddDashboardEndpoints`. The POST accepts `TargetType` of `key`, `connection` or `broadcast`. It rejects sends addressed to the dashboard's own key (403), and it republishes combined metrics after each successful send.
- `GetCombinedMetrics` merges hub counters with `IDashboardService` data. The dashboard's own connection should not be counted as a normal client, so check for that when changing metrics.

**Test host** (`PingDrop.Web.Test/Program.cs`): shows typical wiring and defines the `/send`, `/send-notification` and `/send-to-multiple` endpoints used to exercise the library and dashboard. Its Razor Pages and `wwwroot/lib` content is the stock template, so ignore it.

## Conventions

- Library internals are `internal sealed` and prefixed `PD` (`PDClientConnection`, `PDConnectionStore`). Public contracts live in `Abstractions/`.
- Nullable reference types and implicit usings are enabled in both projects.
- Source files carry a UTF-8 BOM (as written by Visual Studio). Preserve it when editing.
