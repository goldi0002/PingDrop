using PingDrop.Abstractions;
using PingDrop.Models;
using System.Text.Json;

namespace PingDrop.Internal
{
    internal sealed class NotificationFactory : INotificationFactory
    {
        private long _eventId;

        public PDNotification CreateNotification<T>(T notification, string eventType, string? targetKey = null)
        {
            var payloadJson = JsonSerializer.Serialize(notification);
            return new PDNotification
            {
                Id = Interlocked.Increment(ref _eventId),
                EventType = eventType,
                Payload = payloadJson,
                TimestampUtc = DateTime.UtcNow,
                TargetKey = targetKey
            };
        }
    }
}
