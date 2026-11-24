namespace PingDrop.Abstractions
{
    /// <summary>
    /// Names of the telemetry sources PingDrop emits, for use with OpenTelemetry
    /// (<c>AddMeter</c> / <c>AddSource</c>) or any other <c>System.Diagnostics</c> listener.
    /// </summary>
    public static class PingDropTelemetry
    {
        /// <summary>The name of the <see cref="System.Diagnostics.Metrics.Meter"/> that publishes PingDrop metrics.</summary>
        public const string MeterName = "PingDrop";

        /// <summary>The name of the <see cref="System.Diagnostics.ActivitySource"/> that publishes PingDrop traces.</summary>
        public const string ActivitySourceName = "PingDrop";
    }
}
