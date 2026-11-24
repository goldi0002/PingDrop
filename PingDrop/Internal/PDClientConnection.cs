using PingDrop.Abstractions;
using PingDrop.Models;
using System.Threading.Channels;

namespace PingDrop.Internal
{
    internal sealed class PDClientConnection : IAsyncDisposable
    {
        private readonly HashSet<string>? _subscribedEventTypes;
        private readonly PingDropQueueFullMode _fullMode;
        private int _disposed;

        // itemDropped runs synchronously on the writing thread, so a write can tell how many items it displaced.
        [ThreadStatic]
        private static int _droppedByCurrentWrite;

        private long _notificationsSent;
        private long _notificationsDropped;
        private DateTime _lastActivityAtUtc;
        private static long _nextConnectionId;

        public long ConnectionId { get; } = Interlocked.Increment(ref _nextConnectionId);

        public string? SubscriberKey { get; }

        public Channel<PDNotification> NotificationChannel { get; }

        public DateTime ConnectedAtUtc { get; }

        public DateTime LastActivityAtUtc => _lastActivityAtUtc;

        public bool IsDisposed => Volatile.Read(ref _disposed) == 1;

        public int PendingNotifications => NotificationChannel.Reader.Count;

        public long NotificationsSent => Interlocked.Read(ref _notificationsSent);

        public long NotificationsDropped => Interlocked.Read(ref _notificationsDropped);

        public TimeSpan ConnectedDuration => DateTime.UtcNow - ConnectedAtUtc;

        public TimeSpan IdleDuration => DateTime.UtcNow - LastActivityAtUtc;

        public bool HasSubscriptions => _subscribedEventTypes is { Count: > 0 };

        public IReadOnlyCollection<string>? SubscribedEventTypes => _subscribedEventTypes;

        public PDClientConnection() : this(null, null)
        {
        }

        public PDClientConnection(HashSet<string>? eventTypes, string? subscriberKey = null,
            int queueCapacity = PingDropOptions.DefaultClientQueueCapacity,
            PingDropQueueFullMode fullMode = PingDropQueueFullMode.DropOldest)
        {
            _fullMode = fullMode;
            _subscribedEventTypes = eventTypes;

            SubscriberKey = subscriberKey;

            ConnectedAtUtc = DateTime.UtcNow;
            _lastActivityAtUtc = ConnectedAtUtc;

            NotificationChannel = Channel.CreateBounded<PDNotification>(new BoundedChannelOptions(queueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                // DisconnectClient needs TryWrite to fail when full, which is what Wait does.
                FullMode = fullMode switch
                {
                    PingDropQueueFullMode.DropOldest => BoundedChannelFullMode.DropOldest,
                    PingDropQueueFullMode.DropNewest => BoundedChannelFullMode.DropWrite,
                    _ => BoundedChannelFullMode.Wait
                }
            }, _ =>
            {
                _droppedByCurrentWrite++;
                Interlocked.Increment(ref _notificationsDropped);
            });
        }

        public async Task WriteNotificationAsync(PDNotification notification, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await NotificationChannel.Writer.WriteAsync(notification, cancellationToken);

            Touch();
        }

        public bool TryWriteNotification(PDNotification notification)
        {
            return TryWriteNotification(notification, out _);
        }

        /// <summary>
        /// Queues a notification. Returns true when it was queued. <paramref name="dropped"/> is the number of
        /// notifications lost by this call: the displaced oldest one, the rejected new one, or the rejected one that
        /// ended the connection.
        /// </summary>
        public bool TryWriteNotification(PDNotification notification, out int dropped)
        {
            dropped = 0;

            if (IsDisposed)
            {
                Interlocked.Increment(ref _notificationsDropped);
                dropped = 1;
                return false;
            }

            _droppedByCurrentWrite = 0;
            bool written = NotificationChannel.Writer.TryWrite(notification);
            dropped = _droppedByCurrentWrite;

            if (!written)
            {
                // Wait mode refuses a full queue (DisconnectClient ends the stream); a completed channel refuses too.
                Interlocked.Increment(ref _notificationsDropped);
                dropped = 1;

                if (_fullMode == PingDropQueueFullMode.DisconnectClient)
                {
                    _ = DisposeAsync();
                }

                return false;
            }

            // DropWrite reports success even though it discarded this very notification.
            if (_fullMode == PingDropQueueFullMode.DropNewest && dropped > 0)
            {
                return false;
            }

            Touch();
            return true;
        }

        /// <summary>Records connection activity (e.g. a heartbeat) without counting a delivered notification.</summary>
        public void MarkActivity()
        {
            Touch();
        }

        public void MarkDelivered()
        {
            Interlocked.Increment(ref _notificationsSent);
        }

        public bool IsSubscribedTo(string eventType)
        {
            return _subscribedEventTypes == null || _subscribedEventTypes.Contains(eventType);
        }

        private void Touch()
        {
            _lastActivityAtUtc = DateTime.UtcNow;
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(IsDisposed, nameof(PDClientConnection));
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return ValueTask.CompletedTask;
            }

            NotificationChannel.Writer.TryComplete();
            Touch();
            return ValueTask.CompletedTask;
        }
    }
}
