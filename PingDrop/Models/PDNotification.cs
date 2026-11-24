namespace PingDrop.Models
{
    /// <summary>
    /// Represents a notification containing event details, payload data, and metadata.
    /// </summary>
    /// <remarks>This record is immutable and is intended to encapsulate information about an event or message
    /// in a consistent format. It includes details such as the event type, payload, target key, and the timestamp of
    /// the event in UTC.</remarks>
    public sealed class PDNotification
    {
        public long Id { get; init; }
        public string? EventType { get; init; }
        public string? Payload { get; init; }
        /// <summary>Display string for the target: the key, or the keys joined with a comma for a multi-key send. Ambiguous if a key contains a comma; use <see cref="TargetKeys"/> for logic.</summary>
        public string? TargetKey { get; init; }
        /// <summary>The structured list of subscriber keys this event was sent to, or <see langword="null"/> for a broadcast.</summary>
        public IReadOnlyList<string>? TargetKeys { get; init; }
        public DateTime TimestampUtc { get; init; }
    }
}
