namespace WindowsAIAssistant.Application.Voice;

/// <summary>
/// The Application-layer view of voice configuration.
/// <para>
/// The Application project deliberately has no dependency on the Infrastructure
/// configuration types, so the composition root maps <c>VoiceOptions</c> onto this record.
/// That keeps the voice pipeline free of configuration plumbing while still letting the
/// user change behaviour without touching code.
/// </para>
/// </summary>
public sealed record VoiceIntentPolicy
{
    /// <summary>Gets a value indicating whether the voice feature is switched on.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Gets a value indicating whether replies are read aloud.</summary>
    public bool SpeakResponses { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether listening restarts automatically after each command.
    /// Off by default because continuous microphone access must be an explicit choice.
    /// </summary>
    public bool EnableContinuousListening { get; init; }

    /// <summary>Gets the BCP-47 language requested from the recognizer.</summary>
    public string Language { get; init; } = "en-US";

    /// <summary>
    /// Gets the combined recognizer and matcher score below which a command is held back for
    /// confirmation instead of being executed.
    /// </summary>
    public double MinimumCommandConfidence { get; init; } = 0.70;

    /// <summary>Gets a value indicating whether low-confidence commands require confirmation.</summary>
    public bool ConfirmLowConfidenceCommands { get; init; } = true;

    /// <summary>Gets the maximum number of characters read aloud in a single reply.</summary>
    public int MaximumSpokenResponseLength { get; init; } = 240;
}
