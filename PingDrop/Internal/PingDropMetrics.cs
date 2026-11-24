using Microsoft.Extensions.Hosting;
using PingDrop.Abstractions;
using System.Diagnostics.Metrics;

namespace PingDrop.Internal
{
    /// <summary>
    /// Publishes the hub's existing counters as <c>System.Diagnostics.Metrics</c> observable instruments.
    /// Values are read from the hub only when a listener collects, so the delivery hot path is unchanged.
    /// Also an <see cref="IHostedService"/> purely so the singleton is created when the host starts.
    /// </summary>
    internal sealed class PingDropMetrics : IHostedService, IDisposable
    {
        private readonly Meter _meter;
        private readonly bool _ownsMeter;

        public PingDropMetrics(PingDropHub hub, IMeterFactory? meterFactory = null)
        {
            MeterOptions options = new(PingDropTelemetry.MeterName) { Version = InternalConstants.Version };
            _ownsMeter = meterFactory is null;
            _meter = meterFactory?.Create(options) ?? new Meter(options);

            _meter.CreateObservableUpDownCounter("pingdrop.clients.connected", () => hub.ConnectedClientsCount,
                unit: "{client}", description: "Number of SSE clients currently connected (includes the dashboard's own feed when open).");
            _meter.CreateObservableCounter("pingdrop.events.published", () => hub.TotalEventsPublished,
                unit: "{event}", description: "Events published to at least one connected client.");
            _meter.CreateObservableCounter("pingdrop.deliveries.attempted", () => hub.AttemptedDeliveries,
                unit: "{delivery}", description: "Per-client delivery attempts (clients filtered out by event type are not counted).");
            _meter.CreateObservableCounter("pingdrop.deliveries.succeeded", () => hub.SuccessfulDeliveries,
                unit: "{delivery}", description: "Deliveries accepted into a client's queue.");
            _meter.CreateObservableCounter("pingdrop.deliveries.dropped", () => hub.DroppedDeliveries,
                unit: "{delivery}", description: "Deliveries that were dropped instead of delivered.");
        }

        internal Meter Meter => _meter;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose()
        {
            if (_ownsMeter)
            {
                _meter.Dispose();
            }
        }
    }
}
