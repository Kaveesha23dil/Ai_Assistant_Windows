namespace WindowsAIAssistant.Core.Models.Voice;

/// <summary>
/// Provider-independent tuning for a speech recognition session. Only language and silence
/// behaviour are expressed here; recognizer-specific knobs stay inside the implementation.
/// </summary>
public sealed record SpeechRecognitionOptions
{
    /// <summary>The BCP-47 language tag requested from the recognizer.</summary>
    public string Language { get; init; } = "en-US";

    /// <summary>
    /// Gets how long the recognizer waits after the user stops talking before emitting the
    /// final result, in milliseconds.
    /// </summary>
    public int SilenceTimeoutMilliseconds { get; init; } = 1200;

    /// <summary>Gets a value indicating whether partial results are reported while speaking.</summary>
    public bool ReportPartialResults { get; init; } = true;

    /// <summary>Returns the default options for the supplied language.</summary>
    public static SpeechRecognitionOptions For(string? language) => new()
    {
        Language = string.IsNullOrWhiteSpace(language) ? "en-US" : language.Trim()
    };
}
