using WindowsAIAssistant.Core.Abstractions.Time;

namespace WindowsAIAssistant.Infrastructure.Windows;

/// <summary>
/// Supplies the current clock reading. Extracted behind an interface so a test can state a
/// time and assert on the exact sentence the assistant speaks.
/// </summary>
public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
