using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PingDrop.Abstractions;
using PingDrop.Internal;
using PingDrop.Tests.Integration;

namespace PingDrop.Tests.Unit;

public class PingDropMetricsTests
{
    // Filtered to one Meter instance so parallel tests (each with their own meter) cannot interfere.
    private sealed class Collector : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly Dictionary<string, long> _values = new();
        public Collector(Meter meter)
        {
            _listener.InstrumentPublished = (inst, l) => { if (ReferenceEquals(inst.Meter, meter)) l.EnableMeasurementEvents(inst); };
            _listener.SetMeasurementEventCallback<long>((i, v, _, _) => _values[i.Name] = v);
            _listener.SetMeasurementEventCallback<int>((i, v, _, _) => _values[i.Name] = v);
            _listener.Start();
        }
        public Dictionary<string, long> Collect() { _values.Clear(); _listener.RecordObservableInstruments(); return new(_values); }
        public void Dispose() => _listener.Dispose();
    }

    [Fact]
    public async Task Instruments_ReflectHubCounters()
    {
        var store = new PDConnectionStore();
        var hub = new PingDropHub(store, new PDEventHistoryStore(10), new NotificationFactory());
        using var metrics = new PingDropMetrics(hub);
        using var collector = new Collector(metrics.Meter);

        Assert.Equal(PingDropTelemetry.MeterName, metrics.Meter.Name);
        Assert.Equal(0, collector.Collect()["pingdrop.clients.connected"]);

        store.Add(null, "u1");
        store.Add(new HashSet<string> { "other" }, "u2");
        await hub.BroadcastAsync("x", "evt");   // u2 filters it out
        await hub.SendToAsync("u1", "y", "evt");

        var v = collector.Collect();
        Assert.Equal(2, v["pingdrop.clients.connected"]);
        Assert.Equal(2, v["pingdrop.events.published"]);
        Assert.Equal(2, v["pingdrop.deliveries.attempted"]);
        Assert.Equal(2, v["pingdrop.deliveries.succeeded"]);
        Assert.Equal(0, v["pingdrop.deliveries.dropped"]);
    }

    [Fact]
    public async Task Host_UsesMeterFactory_AndPublishesInstruments()
    {
        await using var host = await PingDropHost.StartAsync();
        var metrics = host.Services.GetServices<IHostedService>().OfType<PingDropMetrics>().Single();
        using var collector = new Collector(metrics.Meter);

        await using var stream = await host.ConnectAsync("u1");
        await host.Service.BroadcastAsync("hello", "evt");

        var v = collector.Collect();
        Assert.Equal(1, v["pingdrop.clients.connected"]);
        Assert.Equal(1, v["pingdrop.events.published"]);
        Assert.Equal(1, v["pingdrop.deliveries.succeeded"]);
    }
}
