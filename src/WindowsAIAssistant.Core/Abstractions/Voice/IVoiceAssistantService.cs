using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Core.Abstractions.Voice;

/// <summary>
/// The end-to-end voice assistant as the user interface sees it: it owns the lifecycle state,
/// drives recognition, recognizes intent, routes the command, and speaks the reply.
/// </summary>
public interface IVoiceAssistantService
{
    /// <summary>Gets the current lifecycle state.</summary>
    VoiceAssistantState State { get; }

    /// <summary>
    /// Gets a value indicating whether the voice feature may be used at all, which requires
    /// both the feature switch and microphone consent.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>Gets the most recent response, used to serve a repeat request.</summary>
    string? LastResponse { get; }

    /// <summary>Raised on every lifecycle transition.</summary>
    event EventHandler<VoiceStateChangedEventArgs>? StateChanged;

    /// <summary>Raised for partial and final transcripts so the interface can show progress.</summary>
    event EventHandler<VoiceTranscriptEventArgs>? TranscriptUpdated;

    /// <summary>Raised when a command produced a user-visible response.</summary>
    event EventHandler<VoiceResponseEventArgs>? ResponseProduced;

    /// <summary>Begins capturing speech, stopping any speech in progress first.</summary>
    Task<Result> StartListeningAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops capturing speech and processes the transcript captured so far.</summary>
    Task<Result> StopListeningAsync(CancellationToken cancellationToken = default);

    /// <summary>Abandons the active operation, discards the transcript, and returns to idle.</summary>
    Task<Result> CancelAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops spoken output immediately.</summary>
    Task<Result> StopSpeakingAsync(CancellationToken cancellationToken = default);

    /// <summary>Speaks the most recent response again.</summary>
    Task<Result> RepeatAsync(CancellationToken cancellationToken = default);

    /// <summary>Speaks arbitrary text, for example a read-aloud request.</summary>
    Task<Result> SpeakAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a transcript through intent recognition and command routing. This is the entry
    /// point used by tests and by the chat composer, and it does not require microphone access.
    /// </summary>
    Task<VoiceCommandResult> ProcessTranscriptAsync(
        string transcript,
        CancellationToken cancellationToken = default);
}
