using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Voice;

/// <summary>
/// The outcome of executing a <see cref="VoiceCommand"/>.
/// <para>
/// <see cref="ResponseText"/> is written for speech: it is short, free of markup, and safe
/// to read aloud. Anything sensitive or verbose belongs in <see cref="Data"/>, which is
/// surfaced in the user interface but never spoken.
/// </para>
/// </summary>
public sealed record VoiceCommandResult
{
    private static readonly IReadOnlyDictionary<string, string> NoData =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public VoiceCommandResult(
        Guid commandId,
        AssistantIntent intent,
        bool isSuccess,
        string responseText,
        string? errorCode = null,
        string? errorMessage = null,
        ActionSafetyLevel safetyLevel = ActionSafetyLevel.Safe,
        bool requiresConfirmation = false,
        bool isPermissionDenied = false,
        IReadOnlyDictionary<string, string>? data = null,
        DateTimeOffset? completedAt = null)
    {
        CommandId = commandId;
        Intent = intent;
        IsSuccess = isSuccess;
        ResponseText = responseText ?? string.Empty;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        SafetyLevel = safetyLevel;
        RequiresConfirmation = requiresConfirmation;
        IsPermissionDenied = isPermissionDenied;
        Data = data ?? NoData;
        CompletedAt = completedAt ?? DateTimeOffset.UtcNow;
    }

    /// <summary>Gets the identifier of the command this result belongs to.</summary>
    public Guid CommandId { get; }

    /// <summary>Gets the intent that produced this result.</summary>
    public AssistantIntent Intent { get; }

    /// <summary>Gets a value indicating whether the action completed successfully.</summary>
    public bool IsSuccess { get; }

    /// <summary>Gets a value indicating whether the action failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Gets the short, user-safe text that may be spoken aloud.</summary>
    public string ResponseText { get; }

    /// <summary>Gets the stable error code when the action failed; otherwise <see langword="null"/>.</summary>
    public string? ErrorCode { get; }

    /// <summary>Gets a longer user-safe explanation when the action failed.</summary>
    public string? ErrorMessage { get; }

    /// <summary>Gets the safety classification of the attempted action.</summary>
    public ActionSafetyLevel SafetyLevel { get; }

    /// <summary>
    /// Gets a value indicating whether the action is still waiting for the user to confirm
    /// it. A result with this flag set is a prompt, not a failure.
    /// </summary>
    public bool RequiresConfirmation { get; }

    /// <summary>Gets a value indicating whether a consent switch blocked the action.</summary>
    public bool IsPermissionDenied { get; }

    /// <summary>
    /// Gets non-spoken supporting details such as a saved file path or a result count.
    /// Never contains clipboard text, credentials, or document contents.
    /// </summary>
    public IReadOnlyDictionary<string, string> Data { get; }

    /// <summary>Gets the point in time the action completed.</summary>
    public DateTimeOffset CompletedAt { get; }

    /// <summary>Creates a successful result.</summary>
    public static VoiceCommandResult Success(
        VoiceCommand command,
        string responseText,
        IReadOnlyDictionary<string, string>? data = null) =>
        new(
            command.Id,
            command.Intent,
            isSuccess: true,
            responseText,
            safetyLevel: command.SafetyLevel,
            data: data);

    /// <summary>Creates a successful result for an intent that has no originating command.</summary>
    public static VoiceCommandResult Success(
        AssistantIntent intent,
        string responseText,
        IReadOnlyDictionary<string, string>? data = null) =>
        new(Guid.Empty, intent, isSuccess: true, responseText, data: data);

    /// <summary>Creates a failed result carrying a stable error code.</summary>
    public static VoiceCommandResult Failure(
        VoiceCommand command,
        string errorCode,
        string message,
        bool isPermissionDenied = false) =>
        new(
            command.Id,
            command.Intent,
            isSuccess: false,
            message,
            errorCode,
            message,
            command.SafetyLevel,
            requiresConfirmation: false,
            isPermissionDenied: isPermissionDenied);

    /// <summary>Creates a prompt asking the user to confirm before the action runs.</summary>
    public static VoiceCommandResult NeedsConfirmation(VoiceCommand command, string message) =>
        new(
            command.Id,
            command.Intent,
            isSuccess: false,
            message,
            errorCode: null,
            errorMessage: message,
            command.SafetyLevel,
            requiresConfirmation: true);

    /// <summary>
    /// Creates the standard result for a recognized intent that has no implementation yet,
    /// which is how unsupported and restricted actions are refused.
    /// </summary>
    public static VoiceCommandResult Unavailable(VoiceCommand command, string message) =>
        new(
            command.Id,
            command.Intent,
            isSuccess: false,
            message,
            errorCode: "VOICE_ACTION_NOT_AVAILABLE",
            message,
            command.SafetyLevel,
            requiresConfirmation: command.RequiresConfirmation);
}
