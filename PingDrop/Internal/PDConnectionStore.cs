using PingDrop.Abstractions;
using System.Collections.Concurrent;

namespace PingDrop.Internal
{
    internal sealed class PDConnectionStore : IPDConnectionStore
    {
        private readonly int _queueCapacity;
        private readonly PingDropQueueFullMode _queueFullMode;
        private readonly ConcurrentDictionary<long, PDClientConnection> _all = new();
        // Copy-on-write snapshots, rebuilt on connect/disconnect and never mutated afterwards, so publishing iterates them
        // lock-free and without allocating. _users excludes the dashboard feed.
        private readonly ConcurrentDictionary<string, PDClientConnection[]> _byKey = new();
        private PDClientConnection[] _users = [];
        // All mutations (Add, TryAdd, Remove) run under this lock so the counters, the limit checks and the snapshots stay
        // consistent; reads (GetAll, GetByKey, Count) stay lock-free. Connects and disconnects are rare next to sends.
        private readonly object _gate = new();
        private int _userCount;
        private int _dashboardCount;

        public PDConnectionStore(int queueCapacity = PingDropOptions.DefaultClientQueueCapacity, PingDropQueueFullMode queueFullMode = PingDropQueueFullMode.DropOldest)
        {
            _queueCapacity = queueCapacity;
            _queueFullMode = queueFullMode;
        }

        // Dashboard feed connections are internal and excluded from Count and GetAll; reach them via GetByKey.
        public int Count => Volatile.Read(ref _userCount);
        public int CountByKey(string subscriberKey)
        {
            return !string.IsNullOrWhiteSpace(subscriberKey) && _byKey.TryGetValue(subscriberKey, out var bucket)
            ? bucket.Length
            : 0;
        }

        public PDClientConnection Add(HashSet<string>? eventTypes, string? subscriberKey = null)
        {
            lock (_gate)
            {
                return AddCore(eventTypes, subscriberKey);
            }
        }

        public PDConnectionRejection TryAdd(HashSet<string>? eventTypes, string? subscriberKey, PDConnectionLimits limits, out PDClientConnection? client)
        {
            // Null, empty and whitespace-only keys all mean "no key".
            if (string.IsNullOrWhiteSpace(subscriberKey))
                subscriberKey = null;

            bool dashboard = subscriberKey == InternalConstants.DashboardSubscriberKey;

            lock (_gate)
            {
                if (dashboard)
                {
                    if (limits.MaxDashboardFeeds is int maxFeeds && _dashboardCount >= maxFeeds)
                    {
                        client = null;
                        return PDConnectionRejection.DashboardFeeds;
                    }
                }
                else
                {
                    if (limits.MaxTotal is int maxTotal && _userCount >= maxTotal)
                    {
                        client = null;
                        return PDConnectionRejection.Total;
                    }

                    if (limits.MaxPerKey is int maxPerKey && subscriberKey is not null && CountByKey(subscriberKey) >= maxPerKey)
                    {
                        client = null;
                        return PDConnectionRejection.PerKey;
                    }
                }

                client = AddCore(eventTypes, subscriberKey);
                return PDConnectionRejection.None;
            }
        }

        // Caller holds _gate.
        private PDClientConnection AddCore(HashSet<string>? eventTypes, string? subscriberKey)
        {
            // Null, empty and whitespace-only keys all mean "no key": they are not indexed.
            if (string.IsNullOrWhiteSpace(subscriberKey))
                subscriberKey = null;

            var client = new PDClientConnection(eventTypes, subscriberKey, _queueCapacity, _queueFullMode);

            _all[client.ConnectionId] = client;

            if (client.IsDashboard) _dashboardCount++; else { _userCount++; _users = [.. _users, client]; }

            if (subscriberKey is not null)
            {
                _byKey[subscriberKey] = _byKey.TryGetValue(subscriberKey, out var bucket) ? [.. bucket, client] : [client];
            }

            return client;
        }

        // The returned arrays are shared snapshots: callers must not modify them.
        public PDClientConnection[] GetAll() => Volatile.Read(ref _users);

        public IEnumerable<PDClientConnection> GetAllIncludingDashboard() => _all.Values;

        public PDClientConnection[] GetByKey(string subscriberKey)
        {
            return !string.IsNullOrWhiteSpace(subscriberKey) && _byKey.TryGetValue(subscriberKey, out var bucket)
            ? bucket
            : [];
        }

        public void Remove(long connectionId)
        {
            lock (_gate)
            {
                if (!_all.TryRemove(connectionId, out var client))
                    return;

                if (client.IsDashboard) _dashboardCount--; else { _userCount--; _users = Without(_users, client); }

                if (string.IsNullOrWhiteSpace(client.SubscriberKey))
                    return;

                if (!_byKey.TryGetValue(client.SubscriberKey, out var bucket))
                    return;

                PDClientConnection[] remaining = Without(bucket, client);

                if (remaining.Length == 0)
                    _byKey.TryRemove(client.SubscriberKey, out _);
                else
                    _byKey[client.SubscriberKey] = remaining;
            }
        }

        private static PDClientConnection[] Without(PDClientConnection[] source, PDClientConnection client)
        {
            int index = Array.IndexOf(source, client);
            if (index < 0) return source;
            if (source.Length == 1) return [];

            var result = new PDClientConnection[source.Length - 1];
            Array.Copy(source, 0, result, 0, index);
            Array.Copy(source, index + 1, result, index, source.Length - index - 1);
            return result;
        }
    }
}
