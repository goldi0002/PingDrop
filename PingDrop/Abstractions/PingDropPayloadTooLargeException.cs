namespace PingDrop.Abstractions
{
    /// <summary>
    /// Thrown by <see cref="IPingDropService"/> when a notification's serialized payload is larger than
    /// <see cref="PingDropOptions.MaxPayloadBytes"/>. Nothing is sent, recorded or counted, and no event id is consumed.
    /// </summary>
    public sealed class PingDropPayloadTooLargeException : ArgumentException
    {
        /// <summary>Creates the exception for a payload of <paramref name="size"/> bytes against a limit of <paramref name="limit"/> bytes.</summary>
        public PingDropPayloadTooLargeException(int size, int limit)
            : base($"The notification payload is {size} bytes (UTF-8 JSON), which exceeds PingDropOptions.MaxPayloadBytes ({limit}).", "notification")
        {
            Size = size;
            Limit = limit;
        }

        /// <summary>The serialized payload size in UTF-8 bytes.</summary>
        public int Size { get; }

        /// <summary>The configured limit in bytes.</summary>
        public int Limit { get; }
    }
}
