using PingDrop.Abstractions;
using System.Collections.Concurrent;

namespace PingDrop.Internal
{
    internal sealed class PDConnectionStore : IPDConnectionStore
    {
        private readonly ConcurrentDictionary<long, PDClientConnection> _all = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<long, PDClientConnection>> _byKey = new();
        public int Count => _all.Count;
        public int CountByKey(string subscriberKey)
        {
            return _byKey.TryGetValue(subscriberKey, out var bucket)
            ? bucket.Count
            : 0;
        }

        public PDClientConnection Add(HashSet<string>? eventTypes, string? subscriberKey = null)
        {
            var client = string.IsNullOrEmpty(subscriberKey)
            ? new PDClientConnection(eventTypes)
            : new PDClientConnection(eventTypes, subscriberKey);

            _all[client.ConnectionId] = client;

            if (!string.IsNullOrEmpty(subscriberKey))
            {
                var bucket = _byKey.GetOrAdd(subscriberKey, _ => new ConcurrentDictionary<long, PDClientConnection>());
                bucket[client.ConnectionId] = client;
            }

            return client;
        }

        public IEnumerable<PDClientConnection> GetAll()
        {
            return _all.Values;
        }

        public IEnumerable<PDClientConnection> GetByKey(string subscriberKey)
        {
            return _byKey.TryGetValue(subscriberKey, out var bucket)
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
