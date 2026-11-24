using PingDrop.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace PingDrop.Internal
{
    internal sealed class PDEventPublisher(IPDConnectionStore connectionStore, IEventHistoryStore eventHistoryStore,INotificationFactory notificationFactory, ActivitySource activitySource, ILoggerFactory? loggerFactory = null) : IEventPublisher
    {
        private readonly ILogger _logger = loggerFactory?.CreateLogger(PDLog.Category) ?? NullLogger.Instance;
        private readonly ActivitySource _activitySource = activitySource;
        private readonly IPDConnectionStore _connectionStore = connectionStore;
        private readonly IEventHistoryStore _eventHistoryStore = eventHistoryStore;
        private readonly INotificationFactory _notificationFactory = notificationFactory;

        public ValueTask PublishAsync<T>(T notification, string eventType = "message", CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(notification);
            if (string.IsNullOrWhiteSpace(eventType))
                throw new ArgumentException("Event type cannot be null or empty.", nameof(eventType));
            cancellationToken.ThrowIfCancellationRequested();

            var pdNotification = _notificationFactory.CreateNotification(notification, eventType, InternalConstants.DashboardSubscriberKey);

            using Activity? activity = _activitySource.StartActivity("PingDrop.SendToDashboard", ActivityKind.Internal);
            activity?.SetTag(InternalConstants.EventTypeTag, eventType);
            activity?.SetTag(InternalConstants.EventIdTag, pdNotification.Id);
            activity?.SetTag(InternalConstants.EventPayloadSizeTag, pdNotification.Payload?.Length);
            activity?.SetTag(InternalConstants.ClientsCountTag, _connectionStore.CountByKey(InternalConstants.DashboardSubscriberKey));

            foreach (PDClientConnection client in _connectionStore.GetByKey(InternalConstants.DashboardSubscriberKey))
            {
                if (client.IsSubscribedTo(eventType))
                {
                    try
                    {
                        client.TryWriteNotification(pdNotification);
                    }
                    catch (Exception ex)
                    {
                        PDLog.DashboardWriteFailed(_logger, ex);
                        activity?.SetTag("exception.type", ex.GetType().Name);
                        activity?.SetTag("exception.message", ex.Message);
                        activity?.SetTag("exception.stacktrace", ex.StackTrace);
                    }
                }
            }

            return ValueTask.CompletedTask;
        }
    }
}
