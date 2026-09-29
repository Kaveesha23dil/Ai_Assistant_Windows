using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Vision;

/// <summary>
/// Where the visual assistant is, and why.
/// <para>
/// Carried as one immutable value that the whole operation moves through, so that every layer
/// can report the current phase without a flag per layer and without any of them deciding what
/// the next phase is. Cancellation produces a new <see cref="ScreenAssistantState"/> in
/// <see cref="ScreenAssistantPhase.Idle"/> rather than an error, which is the only way to be
/// sure that a person who pressed Escape is never told something went wrong.
/// </para>
/// </summary>
public sealed record ScreenAssistantState
{
    public static ScreenAssistantState Idle { get; } = new(ScreenAssistantPhase.Idle, null, null);

    public ScreenAssistantState(
        ScreenAssistantPhase phase,
        string? statusMessage = null,
        string? errorCode = null,
        VisualSourceReference? source = null)
    {
        Phase = phase;
        StatusMessage = string.IsNullOrWhiteSpace(statusMessage) ? null : statusMessage;
        ErrorCode = string.IsNullOrWhiteSpace(errorCode) ? null : errorCode;
        Source = source;
    }

    /// <summary>Gets the current phase.</summary>
    public ScreenAssistantPhase Phase { get; }

    /// <summary>Gets a short, user-safe line describing what is happening.</summary>
    public string? StatusMessage { get; }

    /// <summary>Gets the stable code for a failure, when the phase is <see cref="ScreenAssistantPhase.Error"/>.</summary>
    public string? ErrorCode { get; }

    /// <summary>Gets what is being looked at, once something has been captured.</summary>
    public VisualSourceReference? Source { get; }

    /// <summary>Gets a value indicating whether work is in progress.</summary>
    public bool IsBusy => Phase is not (ScreenAssistantPhase.Idle or ScreenAssistantPhase.Completed);

    /// <summary>Gets a value indicating whether a failure is being reported.</summary>
    public bool IsError => Phase == ScreenAssistantPhase.Error;

    /// <summary>Returns the state that means "nothing is happening".</summary>
    public static ScreenAssistantState Cancelled() => Idle;
}
