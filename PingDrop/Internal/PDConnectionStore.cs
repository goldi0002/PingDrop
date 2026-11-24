using PingDrop.Abstractions;
using System.Collections.Concurrent;

namespace PingDrop.Internal
{
    internal sealed class PDConnectionStore : IPDConnectionStore
    {
        private readonly int _queueCapacity;
        private readonly PingDropQueueFullMode _queueFullMode;
        private readonly ConcurrentDictionary<long, PDClientConnection> _all = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<long, PDClientConnection>> _byKey = new();

        public PDConnectionStore(int queueCapacity = PingDropOptions.DefaultClientQueueCapacity, PingDropQueueFullMode queueFullMode = PingDropQueueFullMode.DropOldest)
        {
            _queueCapacity = queueCapacity;
            _queueFullMode = queueFullMode;
        }

        // Dashboard feed connections are internal and excluded from Count and GetAll; reach them via GetByKey.
        public int Count => _all.Values.Count(c => !c.IsDashboard);
        public int CountByKey(string subscriberKey)
        {
            return !string.IsNullOrWhiteSpace(subscriberKey) && _byKey.TryGetValue(subscriberKey, out var bucket)
            ? bucket.Count
            : 0;
        }

        public PDClientConnection Add(HashSet<string>? eventTypes, string? subscriberKey = null)
        {
            // Null, empty and whitespace-only keys all mean "no key": they are not indexed.
            if (string.IsNullOrWhiteSpace(subscriberKey))
                subscriberKey = null;

            var client = new PDClientConnection(eventTypes, subscriberKey, _queueCapacity, _queueFullMode);

            _all[client.ConnectionId] = client;

            if (subscriberKey is not null)
            {
                var bucket = _byKey.GetOrAdd(subscriberKey, _ => new ConcurrentDictionary<long, PDClientConnection>());
                bucket[client.ConnectionId] = client;
            }

            return client;
        }

        public IEnumerable<PDClientConnection> GetAll()
        {
            return _all.Values.Where(c => !c.IsDashboard);
        }

        public IEnumerable<PDClientConnection> GetByKey(string subscriberKey)
        {
            return !string.IsNullOrWhiteSpace(subscriberKey) && _byKey.TryGetValue(subscriberKey, out var bucket)
            ? bucket.Values
            : [];
        }

        public void Remove(long connectionId)
        {
            if (!_all.TryRemove(connectionId, out var client))
                return;

            if (string.IsNullOrWhiteSpace(client.SubscriberKey))
                return;

            if (!_byKey.TryGetValue(client.SubscriberKey, out var bucket))
                return;

            bucket.TryRemove(connectionId, out _);

            if (bucket.IsEmpty)
                _byKey.TryRemove(client.SubscriberKey, out _);
        }
    }
}
