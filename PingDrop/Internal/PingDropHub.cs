using PingDrop.Abstractions;
using PingDrop.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PingDrop.Internal
{
    internal sealed class PingDropHub(IPDConnectionStore connections, IEventHistoryStore eventHistoryStore, INotificationFactory notificationFactory, ILoggerFactory? loggerFactory = null, PingDropOptions? options = null) : IPingDropService
    {
        private static readonly ActivitySource _activitySource = new(InternalConstants.ActivitySourceName, InternalConstants.ActivityVersion);
        private readonly IPDConnectionStore _connectionStore = connections;
        private readonly IEventHistoryStore _eventHistoryStore = eventHistoryStore;
        private readonly INotificationFactory _notificationFactory = notificationFactory;

        private readonly ILogger _logger = loggerFactory?.CreateLogger(PDLog.Category) ?? NullLogger.Instance;
        private readonly object _publishLock = new();
        private readonly bool _replayEnabled = options?.EnableReplay ?? false;
        private readonly int? _maxPayloadBytes = options?.MaxPayloadBytes;
        private long _lastRejectionWarningTicks;
        private long _suppressedRejectionWarnings;
        private long _lastDropWarningTicks;
        private long _suppressedDropWarnings;

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

            Publish(notification, eventType, null, null);

            return ValueTask.CompletedTask;
        }

        public ValueTask SendToAsync<T>(string targetKey, T notification, string eventType = "message", CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetKey);
            ArgumentNullException.ThrowIfNull(notification);
            cancellationToken.ThrowIfCancellationRequested();

            if (targetKey == InternalConstants.DashboardSubscriberKey)
            {
                return ValueTask.CompletedTask;
            }

            Publish(notification, eventType, targetKey, null);

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

            // The dashboard's own feed is never a target.
            keys.RemoveAll(k => k == InternalConstants.DashboardSubscriberKey);

            if (keys.Count == 0)
            {
                return ValueTask.CompletedTask;
            }

            string joinedKeys = string.Join(",", keys);
            Publish(notification, eventType, joinedKeys, keys);

            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Creates, records and delivers one event. Serialization happens first, outside the lock; then the id assignment, the
        /// history entry, the recipient lookup and the delivery run under one lock so that event ids, history order and
        /// per-client queue order all agree. Replay relies on this: a client that registers and then snapshots the history
        /// either sees an event in the snapshot (it was recorded first) or receives it live (it was delivered after the
        /// client registered), and ids in both sources are strictly increasing.
        /// <paramref name="targetKey"/> is null for a broadcast; <paramref name="targetKeys"/> is null for a single-key send
        /// (the notification then carries <c>[targetKey]</c>) and holds the distinct keys for a multi-key send.
        /// </summary>
        private void Publish<T>(T notification, string eventType, string? targetKey, IReadOnlyList<string>? targetKeys)
        {
            string? payloadJson = null;
            if (_maxPayloadBytes is int maxPayloadBytes)
            {
                // With a payload limit, measure before taking the lock, so an oversize payload throws before any id
                // is consumed or anything is recorded, whether or not anyone is connected.
                payloadJson = JsonSerializer.Serialize(notification);
                int size = Encoding.UTF8.GetByteCount(payloadJson);
                if (size > maxPayloadBytes)
                {
                    throw new PingDropPayloadTooLargeException(size, maxPayloadBytes);
                }
            }
            else if (!_replayEnabled && !HasRecipients(targetKey, targetKeys))
            {
                // Nobody to receive it and nothing to record: skip the serialization and the lock.
                return;
            }

            payloadJson ??= JsonSerializer.Serialize(notification);

            lock (_publishLock)
            {
                // Peek at the recipients first so that, with replay off, an event nobody receives does not consume an id.
                if (!_replayEnabled && !HasRecipients(targetKey, targetKeys))
                {
                    return;
                }

                PDNotification pdNotification = _notificationFactory.CreateNotificationFromJson(payloadJson, eventType, targetKey, targetKeys);

                // Record before resolving recipients: a client that registered before this point and snapshots the history
                // later may or may not see this event in the snapshot, but it is then guaranteed to be among the recipients
                // below if it did not.
                _eventHistoryStore.AddEvent(pdNotification);

                PDClientConnection[] clients = ResolveRecipients(targetKey, targetKeys);

                if (clients.Length == 0)
                {
                    // Nobody is connected: the event is kept for replay only. It is not counted as published because it
                    // was not delivered to anyone.
                    return;
                }

                Interlocked.Increment(ref _totalEventsPublished);

                using Activity? activity = _activitySource.StartActivity(targetKey is null ? "PingDrop.Broadcast" : "PingDrop.SendTo", ActivityKind.Internal);

                activity?.SetTag(InternalConstants.EventTypeTag, eventType);
                activity?.SetTag(InternalConstants.EventIdTag, pdNotification.Id);
                activity?.SetTag(InternalConstants.EventPayloadSizeTag, pdNotification.Payload?.Length);
                if (targetKey is not null)
                {
                    activity?.SetTag(InternalConstants.SubscriberKeyTag, targetKey);
                }
                activity?.SetTag(InternalConstants.ClientsCountTag, clients.Length);

                DeliverNotification(pdNotification, eventType, clients);
            }
        }

        private bool HasRecipients(string? targetKey, IReadOnlyList<string>? targetKeys)
        {
            if (targetKey is null) return _connectionStore.GetAll().Length > 0;
            if (targetKeys is null) return _connectionStore.GetByKey(targetKey).Length > 0;

            foreach (string key in targetKeys)
            {
                if (_connectionStore.GetByKey(key).Length > 0) return true;
            }
            return false;
        }

        // A connection has exactly one key and the keys are distinct, so concatenating the per-key snapshots has no duplicates.
        private PDClientConnection[] ResolveRecipients(string? targetKey, IReadOnlyList<string>? targetKeys)
        {
            if (targetKey is null) return _connectionStore.GetAll();
            if (targetKeys is null) return _connectionStore.GetByKey(targetKey);

            PDClientConnection[]? single = null;
            int nonEmpty = 0;
            foreach (string key in targetKeys)
            {
                PDClientConnection[] bucket = _connectionStore.GetByKey(key);
                if (bucket.Length > 0)
                {
                    single ??= bucket;
                    nonEmpty++;
                }
            }
            if (nonEmpty <= 1) return single ?? [];

            var buckets = new PDClientConnection[targetKeys.Count][];
            int total = 0;
            for (int i = 0; i < buckets.Length; i++)
            {
                buckets[i] = _connectionStore.GetByKey(targetKeys[i]);
                total += buckets[i].Length;
            }

            var result = new PDClientConnection[total];
            int offset = 0;
            foreach (PDClientConnection[] bucket in buckets)
            {
                bucket.CopyTo(result, offset);
                offset += bucket.Length;
            }
            return result;
        }

        /// <summary>The newest event id this process has issued (0 when none).</summary>
        public long LastIssuedEventId => _notificationFactory.LastIssuedId;

        /// <summary>
        /// Returns the retained events a client that last saw <paramref name="lastEventId"/> still needs, in id order, limited
        /// to what the client is entitled to receive live. Returns an empty list when <paramref name="lastEventId"/> is newer
        /// than any id this process has issued (for example after a restart, when ids start over).
        /// The caller must have registered the client before calling this.
        /// </summary>
        public IReadOnlyList<PDNotification> GetReplayEvents(PDClientConnection client, long lastEventId)
        {
            if (lastEventId > LastIssuedEventId)
            {
                return [];
            }

            return PDReplay.Select(_eventHistoryStore.GetAll(), client, lastEventId);
        }

        private void DeliverNotification(PDNotification notification, string eventType, PDClientConnection[] clients)
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

                if (client.TryWriteNotification(notification, out int clientDropped))
                {
                    successful++;

                    // An evicted older notification was counted successful when queued; reclassify it so successful + dropped == attempted.
                    if (clientDropped > 0)
                    {
                        successful -= clientDropped;
                        dropped += clientDropped;
                    }
                }
                else
                {
                    // Rejected outright (disposed, DropNewest, or a full queue under DisconnectClient).
                    dropped += clientDropped;
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
                WarnDropped(dropped, attempted, eventType);
            }
        }

        // One aggregate warning per delivery, rate-limited to one per 10 seconds so a stuck client cannot flood the log.
        private void WarnDropped(int dropped, int attempted, string eventType)
        {
            if (!_logger.IsEnabled(LogLevel.Warning))
            {
                return;
            }

            long now = Environment.TickCount64;
            long last = Interlocked.Read(ref _lastDropWarningTicks);

            if ((last != 0 && now - last < 10_000) || Interlocked.CompareExchange(ref _lastDropWarningTicks, now, last) != last)
            {
                Interlocked.Increment(ref _suppressedDropWarnings);
                return;
            }

            PDLog.DeliveriesDropped(_logger, dropped, attempted, eventType, Interlocked.Exchange(ref _suppressedDropWarnings, 0));
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
        /// <summary>Registers a client unless a connection limit is reached (atomically); see <see cref="IPDConnectionStore.TryAdd"/>.</summary>
        public PDConnectionRejection TryAddClient(HashSet<string>? eventTypes, string? subscriberKey, PDConnectionLimits limits, out PDClientConnection? client)
        {
            return _connectionStore.TryAdd(eventTypes, subscriberKey, limits, out client);
        }

        /// <summary>Logs a refused connection as a Warning, at most once per 10 seconds; the rest are counted and reported with the next one.</summary>
        public void WarnConnectionRejected(PDConnectionRejection rejection, PDConnectionLimits limits)
        {
            if (!_logger.IsEnabled(LogLevel.Warning))
            {
                return;
            }

            long now = Environment.TickCount64;
            long last = Interlocked.Read(ref _lastRejectionWarningTicks);

            if ((last != 0 && now - last < 10_000) || Interlocked.CompareExchange(ref _lastRejectionWarningTicks, now, last) != last)
            {
                Interlocked.Increment(ref _suppressedRejectionWarnings);
                return;
            }

            (string reason, int? limit) = rejection switch
            {
                PDConnectionRejection.PerKey => ("MaxConnectionsPerKey", limits.MaxPerKey),
                PDConnectionRejection.Total => ("MaxTotalConnections", limits.MaxTotal),
                _ => ("Dashboard.MaxFeedConnections", limits.MaxDashboardFeeds)
            };
            PDLog.ConnectionRejected(_logger, reason, limit ?? 0, Interlocked.Exchange(ref _suppressedRejectionWarnings, 0));
        }

        public void RemoveClient(long id)
        {
            _connectionStore.Remove(id);
        }
        public void IncrementSuccessfulDispatches()
        {
            Interlocked.Increment(ref _successfulDispatchedDeliveries);
        }
        /// <summary>Counts one event written to a client during replay as an attempted, queued and dispatched delivery (not as a published event).</summary>
        public void RecordReplayedDelivery()
        {
            Interlocked.Increment(ref _attemptedDeliveries);
            Interlocked.Increment(ref _successfulDeliveries);
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
