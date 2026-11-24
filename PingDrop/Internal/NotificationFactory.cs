using PingDrop.Abstractions;
using PingDrop.Models;
using System.Text.Json;

namespace PingDrop.Internal
{
    internal sealed class NotificationFactory : INotificationFactory
    {
        /// <remarks>
        /// Event ids are per-process and monotonic (starting at 1 on every process start). They are not durable
        /// across restarts and must not be used as a persistent or cross-instance identifier.
        /// </remarks>
        private long _eventId;

        public long LastIssuedId => Interlocked.Read(ref _eventId);

        public PDNotification CreateNotification<T>(T notification, string eventType, string? targetKey = null, IReadOnlyList<string>? targetKeys = null)
        {
            return CreateNotificationFromJson(JsonSerializer.Serialize(notification), eventType, targetKey, targetKeys);
        }

        public PDNotification CreateNotificationFromJson(string payloadJson, string eventType, string? targetKey = null, IReadOnlyList<string>? targetKeys = null)
        {
            return new PDNotification
            {
                Id = Interlocked.Increment(ref _eventId),
                EventType = eventType,
                Payload = payloadJson,
                TimestampUtc = DateTime.UtcNow,
                TargetKey = targetKey,
                TargetKeys = targetKeys ?? (targetKey is null ? null : [targetKey])
            };
        }
    }
}
