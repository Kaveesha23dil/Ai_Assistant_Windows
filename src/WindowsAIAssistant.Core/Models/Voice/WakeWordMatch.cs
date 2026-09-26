namespace WindowsAIAssistant.Core.Models.Voice;

/// <summary>
/// A detected wake phrase. The abstraction exists so a future local wake-word engine can be
/// plugged in without touching the listening pipeline; nothing runs it while disabled.
/// </summary>
public sealed record WakeWordMatch
{
    public WakeWordMatch(string phrase, double confidence, DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phrase);

        Phrase = phrase;
        Confidence = Math.Clamp(confidence, 0.0, 1.0);
        OccurredAt = occurredAt;
    }

    /// <summary>Gets the wake phrase that matched.</summary>
    public string Phrase { get; }

    /// <summary>Gets the detector confidence between 0 and 1.</summary>
    public double Confidence { get; }

    /// <summary>Gets the point in time the phrase was detected.</summary>
    public DateTimeOffset OccurredAt { get; }
}
