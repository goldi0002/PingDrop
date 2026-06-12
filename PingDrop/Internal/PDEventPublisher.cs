using PingDrop.Abstractions;
using System.Diagnostics;

namespace PingDrop.Internal
{
    internal sealed class PDEventPublisher(IPDConnectionStore connectionStore, INotificationFactory notificationFactory) : IEventPublisher
    {
        private static readonly ActivitySource _activitySource = new(InternalConstants.ActivitySourceName, InternalConstants.ActivityVersion);
        private readonly IPDConnectionStore _connectionStore = connectionStore;
        private readonly INotificationFactory _notificationFactory = notificationFactory;

        public ValueTask PublishAsync<T>(T notification, string eventType = "message", CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(notification);
            string normalizedEventType = NotificationFactory.NormalizeEventType(eventType);
            cancellationToken.ThrowIfCancellationRequested();

            var pdNotification = _notificationFactory.CreateNotification(notification, normalizedEventType, InternalConstants.DashboardSubscriberKey);

            using Activity? activity = _activitySource.StartActivity("PingDrop.SendToDashboard", ActivityKind.Internal);
            activity?.SetTag(InternalConstants.EventTypeTag, normalizedEventType);
            activity?.SetTag(InternalConstants.EventIdTag, pdNotification.Id);
            activity?.SetTag(InternalConstants.EventPayloadSizeTag, pdNotification.Payload?.Length);
            activity?.SetTag(InternalConstants.ClientsCountTag, _connectionStore.CountByKey(InternalConstants.DashboardSubscriberKey));

            foreach (PDClientConnection client in _connectionStore.GetByKey(InternalConstants.DashboardSubscriberKey))
            {
                if (client.IsSubscribedTo(normalizedEventType))
                {
                    try
                    {
                        client.TryWriteNotification(pdNotification);
                    }
                    catch (Exception ex)
                    {
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
