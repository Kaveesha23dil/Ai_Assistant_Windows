namespace WindowsAIAssistant.Application.Voice;

/// <summary>
/// One immutable set of voice decisions.
/// <para>
/// This is a plain value so it can be built in a single expression and compared, while the
/// policy object the pipeline holds can be pointed at a new one when the person saves a
/// change.
/// </para>
/// </summary>
public sealed record VoiceIntentPolicyValues
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

/// <summary>
/// The Application-layer view of voice configuration.
/// <para>
/// The Application project deliberately has no dependency on the Infrastructure
/// configuration types, so the composition root maps <c>VoiceOptions</c> onto this type.
/// That keeps the voice pipeline free of configuration plumbing while still letting the
/// person change behaviour without touching code.
/// </para>
/// <para>
/// The values are replaced as a whole rather than field by field. Every reader in the
/// pipeline takes this object once and holds it for the life of the session, and a save can
/// arrive at any moment, so a half-applied set of decisions would be read as a real
/// configuration. Publishing one immutable object means a reader either sees the old
/// decisions or the new ones.
/// </para>
/// </summary>
public sealed class VoiceIntentPolicy
{
    private VoiceIntentPolicyValues _values = new();

    /// <summary>Gets the decisions currently in force.</summary>
    public VoiceIntentPolicyValues Values => Volatile.Read(ref _values);

    /// <summary>Gets a value indicating whether the voice feature is switched on.</summary>
    public bool Enabled => Values.Enabled;

    /// <summary>Gets a value indicating whether replies are read aloud.</summary>
    public bool SpeakResponses => Values.SpeakResponses;

    /// <summary>
    /// Gets a value indicating whether listening restarts automatically after each command.
    /// </summary>
    public bool EnableContinuousListening => Values.EnableContinuousListening;

    /// <summary>Gets the BCP-47 language requested from the recognizer.</summary>
    public string Language => Values.Language;

    /// <summary>
    /// Gets the combined recognizer and matcher score below which a command is held back for
    /// confirmation instead of being executed.
    /// </summary>
    public double MinimumCommandConfidence => Values.MinimumCommandConfidence;

    /// <summary>Gets a value indicating whether low-confidence commands require confirmation.</summary>
    public bool ConfirmLowConfidenceCommands => Values.ConfirmLowConfidenceCommands;

    /// <summary>Gets the maximum number of characters read aloud in a single reply.</summary>
    public int MaximumSpokenResponseLength => Values.MaximumSpokenResponseLength;

    /// <summary>
    /// Replaces the decisions in force. Called by the composition root when configuration is
    /// reloaded, and never from inside the pipeline.
    /// </summary>
    public void Update(VoiceIntentPolicyValues values)
    {
        ArgumentNullException.ThrowIfNull(values);

        Volatile.Write(ref _values, values);
    }
}
