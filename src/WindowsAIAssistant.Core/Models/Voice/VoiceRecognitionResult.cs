namespace WindowsAIAssistant.Core.Models.Voice;

/// <summary>
/// A single speech recognition outcome. Partial results stream in while the user is still
/// speaking; the final result is the one that becomes a <see cref="VoiceCommand"/>.
/// </summary>
public sealed record VoiceRecognitionResult
{
    public VoiceRecognitionResult(
        string text,
        bool isFinal,
        double confidence,
        DateTimeOffset timestamp)
    {
        Text = text ?? string.Empty;
        IsFinal = isFinal;
        Confidence = Math.Clamp(confidence, 0.0, 1.0);
        Timestamp = timestamp;
    }

    /// <summary>Gets the recognized text. Empty when nothing intelligible was heard.</summary>
    public string Text { get; }

    /// <summary>Gets a value indicating whether this is the final result for an utterance.</summary>
    public bool IsFinal { get; }

    /// <summary>
    /// Gets the recognizer confidence between 0 and 1. Recognizers that do not report a
    /// score supply 1.0 for an exact result and a lower value for a fuzzy one.
    /// </summary>
    public double Confidence { get; }

    /// <summary>Gets the point in time the result was produced.</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>Gets a value indicating whether the result carries usable text.</summary>
    public bool HasText => !string.IsNullOrWhiteSpace(Text);
}
