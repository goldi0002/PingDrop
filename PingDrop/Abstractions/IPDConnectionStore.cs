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
        /// <summary>A snapshot of the user-facing connections (not the dashboard feed). Shared and immutable: do not modify it.</summary>
        PDClientConnection[] GetAll();
        /// <summary>Every connection including the internal dashboard feed (used for shutdown).</summary>
        IEnumerable<PDClientConnection> GetAllIncludingDashboard();
        /// <summary>A snapshot of the connections registered under a key. Shared and immutable: do not modify it.</summary>
        PDClientConnection[] GetByKey(string subscriberKey);
    }
}
