namespace PingDrop.Internal
{
    /// <summary>Why a connection was refused by <c>IPDConnectionStore.TryAdd</c>.</summary>
    internal enum PDConnectionRejection
    {
        None = 0,
        /// <summary><c>PingDropOptions.MaxConnectionsPerKey</c> reached for the subscriber key.</summary>
        PerKey,
        /// <summary><c>PingDropOptions.MaxTotalConnections</c> reached.</summary>
        Total,
        /// <summary>The dashboard feed cap is reached.</summary>
        DashboardFeeds
    }

    /// <summary>The connection limits to enforce in one <c>TryAdd</c>; <see langword="null"/> means unlimited.</summary>
    internal readonly record struct PDConnectionLimits(int? MaxPerKey, int? MaxTotal, int? MaxDashboardFeeds);
}
