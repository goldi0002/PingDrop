using PingDrop.Abstractions;
using PingDrop.Models;

namespace PingDrop.Internal
{
    internal sealed class PDEventHistoryStore : IEventHistoryStore
    {
        private readonly PDNotification[] _buffer;
        private readonly int _capacity;

        private int _count;
        private int _nextIndex;

        private readonly object _lock = new();

        public PDEventHistoryStore(int capacity)
        {
            _capacity = Math.Max(1, capacity);
            _buffer = new PDNotification[_capacity];
        }

        public int Count => Volatile.Read(ref _count);

        public void AddEvent(PDNotification notification)
        {
            ArgumentNullException.ThrowIfNull(notification);

            lock (_lock)
            {
                _buffer[_nextIndex] = notification;

                _nextIndex++;

                if (_nextIndex == _capacity)
                {
                    _nextIndex = 0;
                }

                if (_count < _capacity)
                {
                    _count++;
                }
            }
        }

        public IReadOnlyCollection<PDNotification> GetAll()
        {
            lock (_lock)
            {
                return GetSnapshot();
            }
        }

        public IReadOnlyCollection<PDNotification> GetRecentEvents(int count)
        {
            if (count <= 0)
            {
                return [];
            }

            lock (_lock)
            {
                return [.. GetSnapshot().TakeLast(count)];
            }
        }

        private PDNotification[] GetSnapshot()
        {
            if (_count == 0)
            {
                return [];
            }

            PDNotification[] result = new PDNotification[_count];

            int start = _count == _capacity
                ? _nextIndex
                : 0;

            for (int i = 0; i < _count; i++)
            {
                result[i] =
                    _buffer[(start + i) % _capacity];
            }

            return result;
        }
    }
}
