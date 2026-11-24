using PingDrop.Internal;

namespace PingDrop.Abstractions
{
    internal interface IPDConnectionStore
    {
        int Count { get; }
        int CountByKey(string subscriberKey);
        PDClientConnection Add(HashSet<string>? eventTypes, string? subscriberKey = null);
        void Remove(long connectionId);
        IEnumerable<PDClientConnection> GetAll();
        /// <summary>Every connection including the internal dashboard feed (used for shutdown).</summary>
        IEnumerable<PDClientConnection> GetAllIncludingDashboard();
        IEnumerable<PDClientConnection> GetByKey(string subscriberKey);
    }
}
