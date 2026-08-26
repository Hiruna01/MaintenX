using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace api.Tests;

/// <summary>
/// Captures log entries so a test can assert on them.
///
/// The allow-list rejection in InternalToolsController is a security control, and the
/// warning it writes is how anyone would ever notice an agent asking for a tool it should
/// not have. That makes the log line part of the behaviour, so it gets a test — which
/// needs somewhere to read the log from.
///
/// Program.cs passes writeToProviders: true to UseSerilog, which is what lets a provider
/// registered here see events that would otherwise go only to Serilog's own sinks.
/// </summary>
public class RecordingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<RecordedLog> _entries = new();

    public IReadOnlyCollection<RecordedLog> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, _entries);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public record RecordedLog(LogLevel Level, string Category, string Message);

    private class RecordingLogger : ILogger
    {
        private readonly string _category;
        private readonly ConcurrentQueue<RecordedLog> _entries;

        public RecordingLogger(string category, ConcurrentQueue<RecordedLog> entries)
        {
            _category = category;
            _entries = entries;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Enqueue(new RecordedLog(logLevel, _category, formatter(state, exception)));
        }
    }
}
