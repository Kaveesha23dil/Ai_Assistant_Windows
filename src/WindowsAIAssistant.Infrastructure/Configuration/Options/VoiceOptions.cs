namespace WindowsAIAssistant.Infrastructure.Configuration.Options;

/// <summary>
/// User-facing voice configuration, bound from the <c>Voice</c> configuration section.
/// <para>
/// Every switch that widens the microphone's reach defaults to off. Continuous listening and
/// wake-word detection in particular are opt-in, because either one turns a single held
/// button into a microphone that is open without a fresh request each time.
/// </para>
/// </summary>
public sealed class VoiceOptions
{
    public const string SectionName = "Voice";

    /// <summary>Gets the master switch for the voice feature.</summary>
    public bool Enabled { get; init; }

    /// <summary>Gets the BCP-47 language tag requested from the recognizer and synthesizer.</summary>
    public string Language { get; init; } = "en-US";

    /// <summary>Gets a value indicating whether replies are read aloud.</summary>
    public bool SpeakResponses { get; init; } = true;

    /// <summary>Gets the preferred voice name, or null to let the platform choose one.</summary>
    public string? SpeechVoice { get; init; }

    /// <summary>Gets the speaking rate, clamped to the range 0.5 to 2.0 by the synthesizer.</summary>
    public double SpeechRate { get; init; } = 1.0;

    /// <summary>Gets the speaking pitch, clamped to the range 0.5 to 2.0 by the synthesizer.</summary>
    public double SpeechPitch { get; init; } = 1.0;

    /// <summary>Gets the spoken output volume between 0.0 and 1.0.</summary>
    public double SpeechVolume { get; init; } = 1.0;

    /// <summary>
    /// Gets a value indicating whether listening restarts automatically after each command.
    /// Off by default so the microphone is only ever open at the user's request.
    /// </summary>
    public bool ContinuousListening { get; init; }

    /// <summary>
    /// Gets a value indicating whether local wake-phrase detection is requested. It stays
    /// unavailable until a local engine is deliberately selected.
    /// </summary>
    public bool WakeWordEnabled { get; init; }

    /// <summary>Gets the wake phrases the assistant would listen for.</summary>
    public string[] WakePhrases { get; init; } = ["hey assistant", "hey nova"];

    /// <summary>
    /// Gets a value indicating whether confirmation-level actions are held back for a
    /// spoken yes before they run.
    /// </summary>
    public bool ConfirmSensitiveActions { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether a low-confidence match is confirmed rather than
    /// executed, so a fuzzy transcript never acts on a guess.
    /// </summary>
    public bool ConfirmLowConfidenceCommands { get; init; } = true;

    /// <summary>Gets the combined recognizer and matcher score below which a command is confirmed.</summary>
    public double MinimumCommandConfidence { get; init; } = 0.70;

    /// <summary>Gets the maximum number of characters read aloud in a single reply.</summary>
    public int MaximumSpokenResponseLength { get; init; } = 240;

    /// <summary>
    /// Gets how long the recognizer waits after the user stops talking before settling the
    /// result, in milliseconds.
    /// </summary>
    public int SilenceTimeoutMilliseconds { get; init; } = 1200;

    /// <summary>
    /// Gets a value indicating whether partial results are reported while the user speaks.
    /// </summary>
    public bool ReportPartialResults { get; init; } = true;

    /// <summary>
    /// Gets the folder screenshots are saved to, or null to use the user's Pictures folder.
    /// </summary>
    public string? ScreenshotFolder { get; init; }
}
