namespace PingDrop.Internal
{
    internal static class InternalConstants
    {
        public const string Version = "v0.9.0";
        #region Default Values

        public const string DefaultEndpointPath = "/pingdrop/stream";
        public const string DefaultEventType = "message";
        public const int DefaultEventHistorySize = 50;
        public const string DefaultQueryEventTypeParam = "eventType";
        #endregion

        public const string ActivitySourceName = PingDrop.Abstractions.PingDropTelemetry.ActivitySourceName;
        public const string ActivityVersion = "v1.1";

        #region Activity Tags
        public const string EventTypeTag = "event.type";
        public const string EventIdTag = "event.id";
        public const string EventPayloadSizeTag = "event.payload.size";
        public const string ClientsCountTag = "clients.count";
        public const string SubscriberKeyTag = "subscriber.key";
        #endregion

        public const string DashboardSubscriberKey = "__pingdrop_dashboard__";
    }
}
