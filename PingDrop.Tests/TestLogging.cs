using Microsoft.Extensions.Logging;

namespace PingDrop.Tests;

public sealed record LogEntry(string Category, LogLevel Level, string Message, Exception? Exception);

/// <summary>Collects every log entry so tests can assert on category, level and exception.</summary>
public sealed class LogCollector : ILoggerProvider
{
    private readonly List<LogEntry> _entries = new();

    public IReadOnlyList<LogEntry> Entries { get { lock (_entries) return _entries.ToList(); } }

    public ILogger CreateLogger(string categoryName) => new CollectorLogger(categoryName, this);

    public void Dispose() { }

    private sealed class CollectorLogger(string category, LogCollector owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (owner._entries) owner._entries.Add(new LogEntry(category, logLevel, formatter(state, exception), exception));
        }
    }
}
