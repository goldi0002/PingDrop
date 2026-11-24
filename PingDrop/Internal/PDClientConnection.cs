using PingDrop.Models;
using System.Threading.Channels;

namespace PingDrop.Internal
{
    internal sealed class PDClientConnection : IAsyncDisposable
    {
        private readonly HashSet<string>? _subscribedEventTypes;
        private int _disposed;

        private long _notificationsSent;
        private long _notificationsDropped;
        private DateTime _lastActivityAtUtc;
        private static long _nextConnectionId;

        /// <summary>Maximum number of undelivered notifications queued per client before the oldest is dropped.</summary>
        internal const int QueueCapacity = 20;

        // The itemDropped callback runs synchronously on the writing thread, so a thread-static tally lets a write report its own overflow.
        [ThreadStatic]
        private static int _overflowOnCurrentWrite;

        public long ConnectionId { get; } = Interlocked.Increment(ref _nextConnectionId);

        public string? SubscriberKey { get; }

        /// <summary>True for the dashboard's own live feed, which is internal and not a user-facing client.</summary>
        public bool IsDashboard => SubscriberKey == InternalConstants.DashboardSubscriberKey;

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

        public PDClientConnection(HashSet<string>? eventTypes, string? subscriberKey = null)
        {
            _subscribedEventTypes = eventTypes;

            SubscriberKey = subscriberKey;

            ConnectedAtUtc = DateTime.UtcNow;
            _lastActivityAtUtc = ConnectedAtUtc;

            NotificationChannel = Channel.CreateBounded<PDNotification>(new BoundedChannelOptions(QueueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest
            }, OnItemDropped);
        }

        public async Task WriteNotificationAsync(PDNotification notification, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await NotificationChannel.Writer.WriteAsync(notification, cancellationToken);

            Touch();
        }

        // With DropOldest, TryWrite always succeeds, so overflow is only observable through this callback.
        private void OnItemDropped(PDNotification _)
        {
            Interlocked.Increment(ref _notificationsDropped);
            _overflowOnCurrentWrite++;
        }

        public bool TryWriteNotification(PDNotification notification)
        {
            return TryWriteNotification(notification, out _);
        }

        /// <summary>Queues a notification; <paramref name="overflowDropped"/> is the number of older queued notifications evicted to make room.</summary>
        public bool TryWriteNotification(PDNotification notification, out int overflowDropped)
        {
            overflowDropped = 0;

            if (IsDisposed)
            {
                return false;
            }

            _overflowOnCurrentWrite = 0;
            bool written = NotificationChannel.Writer.TryWrite(notification);
            overflowDropped = _overflowOnCurrentWrite;
            _overflowOnCurrentWrite = 0;

            if (written)
            {
                Touch();
            }
            else
            {
                Interlocked.Increment(ref _notificationsDropped);
            }

            return written;
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
