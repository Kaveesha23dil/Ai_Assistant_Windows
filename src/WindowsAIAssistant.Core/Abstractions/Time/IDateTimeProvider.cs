namespace WindowsAIAssistant.Core.Abstractions.Time;

/// <summary>
/// Supplies the current clock time to improve testability.
/// </summary>
public interface IDateTimeProvider
{
    /// <summary>Gets the current Coordinated Universal Time.</summary>
    DateTimeOffset UtcNow { get; }
}