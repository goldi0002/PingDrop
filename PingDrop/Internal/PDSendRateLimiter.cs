namespace PingDrop.Internal
{
    /// <summary>
    /// A small fixed-window limiter: at most <c>limit</c> permits per one-minute window, counted from the first permit of
    /// each window. Thread-safe; the clock is injectable for tests.
    /// </summary>
    internal sealed class PDSendRateLimiter(int limit, TimeProvider timeProvider)
    {
        private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
        private readonly object _gate = new();
        private long _windowStart;
        private bool _started;
        private int _count;

        /// <summary>Takes a permit. When none is left, returns false and the whole seconds (at least 1) until the window resets.</summary>
        public bool TryAcquire(out int retryAfterSeconds)
        {
            long now = timeProvider.GetTimestamp();

            lock (_gate)
            {
                if (!_started || timeProvider.GetElapsedTime(_windowStart, now) >= Window)
                {
                    _started = true;
                    _windowStart = now;
                    _count = 0;
                }

                if (_count < limit)
                {
                    _count++;
                    retryAfterSeconds = 0;
                    return true;
                }

                TimeSpan remaining = Window - timeProvider.GetElapsedTime(_windowStart, now);
                retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
                return false;
            }
        }
    }
}
