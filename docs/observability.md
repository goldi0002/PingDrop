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
