using PingDrop.Models;
using System.Threading.Channels;

namespace PingDrop.Internal
{
    internal sealed class PDClientConnection : IAsyncDisposable
    {
        private const int DefaultQueueCapacity = 20;

        private readonly HashSet<string>? _subscribedEventTypes;
        private int _disposed;

        private long _notificationsSent;
        private long _notificationsDropped;
        private long _lastActivityAtUtcTicks;
        private static long _nextConnectionId;

        public long ConnectionId { get; } = Interlocked.Increment(ref _nextConnectionId);

        public string? SubscriberKey { get; }

        public Channel<PDNotification> NotificationChannel { get; }

        public DateTime ConnectedAtUtc { get; }

        public DateTime LastActivityAtUtc => new(Interlocked.Read(ref _lastActivityAtUtcTicks), DateTimeKind.Utc);

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
            _lastActivityAtUtcTicks = ConnectedAtUtc.Ticks;

            NotificationChannel = Channel.CreateBounded<PDNotification>(new BoundedChannelOptions(DefaultQueueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
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

        public void MarkDelivered()
        {
            Interlocked.Increment(ref _notificationsSent);
            Touch();
        }

        public bool IsSubscribedTo(string eventType)
        {
            return _subscribedEventTypes == null || _subscribedEventTypes.Contains(eventType);
        }

        private void Touch()
        {
            Interlocked.Exchange(ref _lastActivityAtUtcTicks, DateTime.UtcNow.Ticks);
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
