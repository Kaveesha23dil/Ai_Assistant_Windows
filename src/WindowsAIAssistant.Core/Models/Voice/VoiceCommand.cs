using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Voice;

/// <summary>
/// A transcript that has been converted into an executable intent, together with the
/// parameters the selected handler needs.
/// <para>
/// This is the only structure a voice handler receives. Handlers never see the raw
/// recognizer event, and they never accept free-form shell input.
/// </para>
/// </summary>
public sealed record VoiceCommand
{
    /// <summary>Parameter name carrying a resolved application name.</summary>
    public const string ApplicationParameter = "application";

    /// <summary>Parameter name carrying a well-known folder identifier.</summary>
    public const string FolderParameter = "folder";

    /// <summary>Parameter name carrying a Windows Settings page name.</summary>
    public const string SettingParameter = "setting";

    /// <summary>Parameter name carrying a free-text search query.</summary>
    public const string QueryParameter = "query";

    /// <summary>Parameter name carrying a numeric volume percentage.</summary>
    public const string VolumeParameter = "volume";

    /// <summary>Parameter name carrying the target web search provider.</summary>
    public const string SearchProviderParameter = "provider";

    /// <summary>Parameter name carrying text that should be spoken aloud.</summary>
    public const string TextParameter = "text";

    public VoiceCommand(
        Guid id,
        string originalText,
        AssistantIntent intent,
        IReadOnlyDictionary<string, string> parameters,
        double confidence,
        DateTimeOffset timestamp,
        ActionSafetyLevel safetyLevel,
        bool requiresConfirmation,
        bool isConfirmed = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalText);

        Id = id;
        OriginalText = originalText;
        Intent = intent;
        Parameters = parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Confidence = Math.Clamp(confidence, 0.0, 1.0);
        Timestamp = timestamp;
        SafetyLevel = safetyLevel;
        RequiresConfirmation = requiresConfirmation;
        IsConfirmed = isConfirmed;
    }

    /// <summary>Gets the unique identifier of this command.</summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the transcript exactly as the user produced it. It is never written to a log
    /// sink; it is only shown to the user or spoken back for confirmation.
    /// </summary>
    public string OriginalText { get; init; }

    /// <summary>Gets the recognized intent.</summary>
    public AssistantIntent Intent { get; init; }

    /// <summary>Gets the intent parameters, keyed case-insensitively.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; }

    /// <summary>
    /// Gets the combined recognizer and matcher confidence between 0 and 1. Commands below
    /// the configured threshold are held back for confirmation rather than executed.
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>Gets the point in time the command was created.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Gets the safety classification of the requested action.</summary>
    public ActionSafetyLevel SafetyLevel { get; init; }

    /// <summary>
    /// Gets a value indicating whether the action must be confirmed before it runs. This is
    /// set for confirmation-level actions and for low-confidence matches.
    /// </summary>
    public bool RequiresConfirmation { get; init; }

    /// <summary>
    /// Gets a value indicating whether the user has already confirmed this command. The
    /// voice assistant sets it when the user answers a confirmation prompt.
    /// </summary>
    public bool IsConfirmed { get; init; }

    /// <summary>Creates a command with a new identifier and the current time.</summary>
    public static VoiceCommand Create(
        string originalText,
        AssistantIntent intent,
        IReadOnlyDictionary<string, string>? parameters = null,
        double confidence = 1.0,
        ActionSafetyLevel safetyLevel = ActionSafetyLevel.Safe,
        bool requiresConfirmation = false,
        DateTimeOffset? timestamp = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalText);

        return new VoiceCommand(
            Guid.NewGuid(),
            originalText,
            intent,
            parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            confidence,
            timestamp ?? DateTimeOffset.UtcNow,
            safetyLevel,
            requiresConfirmation);
    }

    /// <summary>Returns the named parameter, or <see langword="null"/> when it is absent or empty.</summary>
    public string? GetParameter(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return Parameters.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    /// <summary>Attempts to read the named parameter as a 32-bit integer.</summary>
    public bool TryGetInt32(string name, out int value)
    {
        value = 0;

        var raw = GetParameter(name);
        return raw is not null && int.TryParse(raw, out value);
    }
}
