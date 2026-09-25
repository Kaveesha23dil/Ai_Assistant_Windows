using Microsoft.Extensions.Logging;

namespace WindowsAIAssistant.Application.Tests.Helpers;

/// <summary>A single captured log entry.</summary>
public sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

/// <summary>
/// A minimal capturing logger used only where a test needs to assert logging behavior.
/// </summary>
public sealed class TestLogger<T> : ILogger<T>
{
    private readonly List<LogEntry> _entries = [];

    public IReadOnlyList<LogEntry> Entries => _entries;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        _entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
    }

    public bool Contains(LogLevel level, string messageFragment) =>
        _entries.Any(entry => entry.Level == level
            && entry.Message.Contains(messageFragment, StringComparison.OrdinalIgnoreCase));

    public bool ContainsMessage(string messageFragment) =>
        _entries.Any(entry => entry.Message.Contains(messageFragment, StringComparison.OrdinalIgnoreCase));

    public bool ContainsLevel(LogLevel level) => _entries.Any(entry => entry.Level == level);
}
