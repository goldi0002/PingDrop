# Observability

PingDrop publishes metrics through `System.Diagnostics.Metrics` and traces through `System.Diagnostics.ActivitySource`, so it works with OpenTelemetry or any other listener (`dotnet-counters`, `MeterListener`) without extra packages. The names are available as constants on `PingDrop.Abstractions.PingDropTelemetry`.

| Signal | Name |
|---|---|
| Meter | `PingDrop` (`PingDropTelemetry.MeterName`) |
| ActivitySource | `PingDrop` (`PingDropTelemetry.ActivitySourceName`) |

## OpenTelemetry registration

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter(PingDropTelemetry.MeterName))   // or "PingDrop"
    .WithTracing(t => t.AddSource(PingDropTelemetry.ActivitySourceName));
```

## Instruments

All instruments are observable: values are read from the hub's existing counters when a collector polls, so publishing costs nothing on the send path. The meter is created through `IMeterFactory` when one is registered (it is by default in ASP.NET Core).

| Instrument | Type | Unit | Description |
|---|---|---|---|
| `pingdrop.clients.connected` | ObservableUpDownCounter&lt;int&gt; | `{client}` | SSE clients currently connected. The dashboard's own live feed is not counted. |
| `pingdrop.events.published` | ObservableCounter&lt;long&gt; | `{event}` | Events published to at least one connected client. Sends with no matching client are not counted. |
| `pingdrop.deliveries.attempted` | ObservableCounter&lt;long&gt; | `{delivery}` | Per-client delivery attempts. Clients that filtered out the event type are not counted. |
| `pingdrop.deliveries.succeeded` | ObservableCounter&lt;long&gt; | `{delivery}` | Deliveries accepted into a client's queue. |
| `pingdrop.deliveries.dropped` | ObservableCounter&lt;long&gt; | `{delivery}` | Deliveries dropped instead of delivered. |

These are the same numbers the [dashboard](dashboard.md) shows.

## Logging

PingDrop logs through `ILogger` using source-generated messages. Every logger uses the single category **`PingDrop`**, so one filter controls all of it:

```json
{ "Logging": { "LogLevel": { "Default": "Information", "PingDrop": "Warning" } } }
```

Messages never contain notification payloads. Each has a stable `EventId` you can filter or alert on.

| EventId | Level | When |
|---|---|---|
| 1 | Debug | A client connected (connection id, subscriber key) |
| 2 | Debug | A client disconnected |
| 3 | Error | Your `ClientConnected` callback threw; the connection stays open |
| 4 | Error | Your `ClientDisconnected` callback threw |
| 5 | Debug | Publishing a live log entry to the dashboard failed |
| 6 | Warning | Deliveries were dropped because client queues were full or closed. Rate-limited: the message says how many further warnings were suppressed |
| 7 | Error | The dashboard could not list connections |
| 8 | Warning | Writing a notification to a dashboard client failed |
| 9 | Debug | Missed events were replayed after `Last-Event-ID` |
| 10 | Debug | Replay skipped because `Last-Event-ID` is newer than any id issued (ids restart with the process) |
| 11 | Warning | A connection was refused because `MaxConnectionsPerKey` or `MaxTotalConnections` is reached. Rate-limited like 6 |

At the default `Information` level PingDrop is quiet; the warnings (6 and 11) are the ones worth alerting on. The dashboard's Live Logs page has its own feed, controlled by `Dashboard.LogEachDispatch`, and is separate from `ILogger`.
