using Microsoft.Extensions.Logging;

namespace PingDrop.Internal
{
    /// <summary>Source-generated log messages. All PingDrop loggers use the category <see cref="Category"/>.</summary>
    internal static partial class PDLog
    {
        public const string Category = "PingDrop";

        [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Client {ConnectionId} connected (subscriber key: {SubscriberKey}).")]
        public static partial void ClientConnected(ILogger logger, long connectionId, string? subscriberKey);

        [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Client {ConnectionId} disconnected.")]
        public static partial void ClientDisconnected(ILogger logger, long connectionId);

        [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "The ClientConnected callback threw for connection {ConnectionId}; the connection stays open.")]
        public static partial void ClientConnectedCallbackFailed(ILogger logger, long connectionId, Exception exception);

        [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "The ClientDisconnected callback threw for connection {ConnectionId}.")]
        public static partial void ClientDisconnectedCallbackFailed(ILogger logger, long connectionId, Exception exception);

        [LoggerMessage(EventId = 5, Level = LogLevel.Debug, Message = "Failed to publish a live log entry to the dashboard.")]
        public static partial void LiveLogPublishFailed(ILogger logger, Exception exception);

        [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Dropped {Dropped} of {Attempted} deliveries for event '{EventType}' because client queues were full or closed ({Suppressed} further drop warnings suppressed since the last one).")]
        public static partial void DeliveriesDropped(ILogger logger, int dropped, int attempted, string eventType, long suppressed);

        [LoggerMessage(EventId = 7, Level = LogLevel.Error, Message = "Failed to retrieve dashboard connections.")]
        public static partial void DashboardConnectionsFailed(ILogger logger, Exception exception);

        [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "Failed to write a notification to a dashboard client.")]
        public static partial void DashboardWriteFailed(ILogger logger, Exception exception);
    }
}
