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

            NotificationChannel = Channel.CreateBounded<PDNotification>(new BoundedChannelOptions(20)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest
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
            if (IsDisposed)
            {
                return false;
            }

            bool written = NotificationChannel.Writer.TryWrite(notification);

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
