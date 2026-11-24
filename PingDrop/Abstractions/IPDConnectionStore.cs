using PingDrop.Internal;

namespace PingDrop.Abstractions
{
    internal interface IPDConnectionStore
    {
        int Count { get; }
        int CountByKey(string subscriberKey);
        PDClientConnection Add(HashSet<string>? eventTypes, string? subscriberKey = null);
        /// <summary>
        /// Registers a connection unless a limit in <paramref name="limits"/> is reached. The check and the registration are one
        /// atomic step, so concurrent callers cannot exceed a limit. Returns <see cref="PDConnectionRejection.None"/> on success.
        /// </summary>
        PDConnectionRejection TryAdd(HashSet<string>? eventTypes, string? subscriberKey, PDConnectionLimits limits, out PDClientConnection? client);
        void Remove(long connectionId);
        IEnumerable<PDClientConnection> GetAll();
        /// <summary>Every connection including the internal dashboard feed (used for shutdown).</summary>
        IEnumerable<PDClientConnection> GetAllIncludingDashboard();
        IEnumerable<PDClientConnection> GetByKey(string subscriberKey);
    }
}
