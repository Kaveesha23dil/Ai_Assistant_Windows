using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Voice;

/// <summary>
/// One retained entry in the in-memory voice command history.
/// <para>
/// Deliberately excludes the transcript, the spoken response, clipboard text, and AI prompt
/// content. Retaining the intent, outcome, and timing is enough to answer "what did the
/// assistant just do" without keeping a record of what was said.
/// </para>
/// </summary>
public sealed record VoiceCommandHistoryEntry
{
    public VoiceCommandHistoryEntry(
        Guid commandId,
        AssistantIntent intent,
        ActionSafetyLevel safetyLevel,
        bool isSuccessful,
        string? errorCode,
        DateTimeOffset occurredAt)
    {
        CommandId = commandId;
        Intent = intent;
        SafetyLevel = safetyLevel;
        IsSuccessful = isSuccessful;
        ErrorCode = errorCode;
        OccurredAt = occurredAt;
    }

    /// <summary>Gets the identifier of the command.</summary>
    public Guid CommandId { get; }

    /// <summary>Gets the intent that ran.</summary>
    public AssistantIntent Intent { get; }

    /// <summary>Gets the safety classification of the action.</summary>
    public ActionSafetyLevel SafetyLevel { get; }

    /// <summary>Gets a value indicating whether the action succeeded.</summary>
    public bool IsSuccessful { get; }

    /// <summary>Gets the stable error code when the action failed.</summary>
    public string? ErrorCode { get; }

    /// <summary>Gets the point in time the action completed.</summary>
    public DateTimeOffset OccurredAt { get; }
}
