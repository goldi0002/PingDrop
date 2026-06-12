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
            string normalizedEventType = NormalizeEventType(eventType);
            var payloadJson = JsonSerializer.Serialize(notification);
            return new PDNotification
            {
                Id = Interlocked.Increment(ref _eventId),
                EventType = normalizedEventType,
                Payload = payloadJson,
                TimestampUtc = DateTime.UtcNow,
                TargetKey = targetKey
            };
        }

        internal static string NormalizeEventType(string eventType)
        {
            if (string.IsNullOrWhiteSpace(eventType))
            {
                throw new ArgumentException("Event type cannot be null or empty.", nameof(eventType));
            }

            string normalized = eventType.Trim();

            if (normalized.Contains('\r') || normalized.Contains('\n'))
            {
                throw new ArgumentException("Event type cannot contain line breaks.", nameof(eventType));
            }

            return normalized;
        }
    }
}
