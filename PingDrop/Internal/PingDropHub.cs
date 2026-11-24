using PingDrop.Abstractions;
using PingDrop.Models;
using System.Diagnostics;

namespace PingDrop.Internal
{
    internal sealed class PingDropHub(IPDConnectionStore connections, IEventHistoryStore eventHistoryStore, INotificationFactory notificationFactory) : IPingDropService
    {
        private static readonly ActivitySource _activitySource = new(InternalConstants.ActivitySourceName, InternalConstants.ActivityVersion);
        private readonly IPDConnectionStore _connectionStore = connections;
        private readonly IEventHistoryStore _eventHistoryStore = eventHistoryStore;
        private readonly INotificationFactory _notificationFactory = notificationFactory;

        private long _totalEventsPublished;
        private long _attemptedDeliveries;
        private long _successfulDeliveries;
        private long _droppedDeliveries;
        private long _successfulDispatchedDeliveries;
        public long TotalEventsPublished => Interlocked.Read(ref _totalEventsPublished);
        public long SuccessfulDispatchedDeliveries => Interlocked.Read(ref _successfulDispatchedDeliveries);
        public long AttemptedDeliveries => Interlocked.Read(ref _attemptedDeliveries);
        public long SuccessfulDeliveries => Interlocked.Read(ref _successfulDeliveries);
        public long DroppedDeliveries => Interlocked.Read(ref _droppedDeliveries);

        public int ConnectedClientsCount => _connectionStore.Count;

        public ValueTask BroadcastAsync<T>(T notification, string eventType = "message", CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(notification);
            cancellationToken.ThrowIfCancellationRequested();

            if (_connectionStore.Count == 0)
            {
                return ValueTask.CompletedTask;
            }

            PDNotification pdNotification = _notificationFactory.CreateNotification(notification, eventType);

            Interlocked.Increment(ref _totalEventsPublished);
            _eventHistoryStore.AddEvent(pdNotification);

            using Activity? activity = _activitySource.StartActivity("PingDrop.Broadcast", ActivityKind.Internal);

            activity?.SetTag(InternalConstants.EventTypeTag, eventType);
            activity?.SetTag(InternalConstants.EventIdTag, pdNotification.Id);
            activity?.SetTag(InternalConstants.EventPayloadSizeTag, pdNotification.Payload?.Length);
            activity?.SetTag(InternalConstants.ClientsCountTag, _connectionStore.Count);

            DeliverNotification(pdNotification, eventType, _connectionStore.GetAll());

            return ValueTask.CompletedTask;
        }

        public ValueTask SendToAsync<T>(string targetKey, T notification, string eventType = "message", CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetKey);
            ArgumentNullException.ThrowIfNull(notification);
            cancellationToken.ThrowIfCancellationRequested();

            int clientCount = _connectionStore.CountByKey(targetKey);

            if (clientCount == 0)
            {
                return ValueTask.CompletedTask;
            }

            PDNotification pdNotification = _notificationFactory.CreateNotification(notification, eventType, targetKey);
            Interlocked.Increment(ref _totalEventsPublished);
            _eventHistoryStore.AddEvent(pdNotification);

            using Activity? activity = _activitySource.StartActivity("PingDrop.SendTo", ActivityKind.Internal);

            activity?.SetTag(InternalConstants.EventTypeTag, eventType);
            activity?.SetTag(InternalConstants.EventIdTag, pdNotification.Id);
            activity?.SetTag(InternalConstants.EventPayloadSizeTag, pdNotification.Payload?.Length);
            activity?.SetTag(InternalConstants.SubscriberKeyTag, targetKey);
            activity?.SetTag(InternalConstants.ClientsCountTag, clientCount);

            DeliverNotification(pdNotification, eventType, _connectionStore.GetByKey(targetKey));

            return ValueTask.CompletedTask;
        }

        public ValueTask SendToAsync<T>(IEnumerable<string> targetKeys, T notification, string eventType = "message", CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(targetKeys);
            ArgumentNullException.ThrowIfNull(notification);
            cancellationToken.ThrowIfCancellationRequested();

            List<string> keys = targetKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal).ToList();

            if (keys.Count == 0)
            {
                throw new ArgumentException("At least one non-blank target key is required.", nameof(targetKeys));
            }

            List<PDClientConnection> clients = keys.SelectMany(_connectionStore.GetByKey).Distinct().ToList();

            if (clients.Count == 0)
            {
                return ValueTask.CompletedTask;
            }

            string joinedKeys = string.Join(",", keys);
            PDNotification pdNotification = _notificationFactory.CreateNotification(notification, eventType, joinedKeys);
            Interlocked.Increment(ref _totalEventsPublished);
            _eventHistoryStore.AddEvent(pdNotification);

            using Activity? activity = _activitySource.StartActivity("PingDrop.SendTo", ActivityKind.Internal);

            activity?.SetTag(InternalConstants.EventTypeTag, eventType);
            activity?.SetTag(InternalConstants.EventIdTag, pdNotification.Id);
            activity?.SetTag(InternalConstants.EventPayloadSizeTag, pdNotification.Payload?.Length);
            activity?.SetTag(InternalConstants.SubscriberKeyTag, joinedKeys);
            activity?.SetTag(InternalConstants.ClientsCountTag, clients.Count);

            DeliverNotification(pdNotification, eventType, clients);

            return ValueTask.CompletedTask;
        }

        private void DeliverNotification(PDNotification notification, string eventType, IEnumerable<PDClientConnection> clients)
        {
            int attempted = 0;
            int successful = 0;
            int dropped = 0;

            foreach (PDClientConnection client in clients)
            {
                if (client.HasSubscriptions && !client.IsSubscribedTo(eventType))
                {
                    continue;
                }

                attempted++;

                if (client.TryWriteNotification(notification))
                {
                    successful++;
                }
                else
                {
                    dropped++;
                }
            }

            if (attempted > 0)
            {
                Interlocked.Add(ref _attemptedDeliveries, attempted);
            }

            if (successful > 0)
            {
                Interlocked.Add(ref _successfulDeliveries, successful);
            }

            if (dropped > 0)
            {
                Interlocked.Add(ref _droppedDeliveries, dropped);
            }
        }
        public PDHubMetrics GetMetrics()
        {
            return new PDHubMetrics(ConnectedClientsCount, Interlocked.Read(ref _attemptedDeliveries), Interlocked.Read(ref _successfulDeliveries), Interlocked.Read(ref _droppedDeliveries));
        }

        #region Client Management

        public PDClientConnection AddClient(HashSet<string>? eventTypes, string? subscriberKey)
        {
            return _connectionStore.Add(eventTypes, subscriberKey);
        }
        public void RemoveClient(long id)
        {
            _connectionStore.Remove(id);
        }
        public void IncrementSuccessfulDispatches()
        {
            Interlocked.Increment(ref _successfulDispatchedDeliveries);
        }
        public void IncrementDroppedDispatches()
        {
            Interlocked.Increment(ref _droppedDeliveries);
        }

        #endregion
    }
    /// <summary>
    /// Represents metrics related to the operation of a PDHub, including client connections and message delivery
    /// statistics.
    /// </summary>
    /// <param name="ConnectedClients">The number of clients currently connected to the PDHub.</param>
    /// <param name="AttemptedDeliveries">The total number of message delivery attempts made by the PDHub.</param>
    /// <param name="SuccessfulDeliveries">The total number of messages successfully delivered by the PDHub.</param>
    /// <param name="DroppedDeliveries">The total number of messages dropped by the PDHub due to delivery failures.</param>
    public sealed record PDHubMetrics(int ConnectedClients, long AttemptedDeliveries, long SuccessfulDeliveries, long DroppedDeliveries);
}
