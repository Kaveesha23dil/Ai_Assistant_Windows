using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Core.Abstractions.Voice;

/// <summary>
/// Captures speech and turns it into transcripts.
/// <para>
/// The interface is intentionally free of recognizer types so a local, on-device engine and
/// a cloud engine are interchangeable. Implementations raise the transcript events on the
/// thread that owns the recognizer; consumers must marshal to their own thread.
/// </para>
/// </summary>
public interface ISpeechRecognitionService
{
    /// <summary>Gets a value indicating whether a recognition session is active.</summary>
    bool IsListening { get; }

    /// <summary>
    /// Gets a value indicating whether recognition can run on this machine right now. A
    /// missing microphone or a denied privacy setting both make this false.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>Gets the BCP-47 language tag currently in use.</summary>
    string Language { get; }

    /// <summary>Raised once a recognition session has actually started capturing audio.</summary>
    event EventHandler<VoiceStateChangedEventArgs>? ListeningStarted;

    /// <summary>Raised once a recognition session has finished, normally or otherwise.</summary>
    event EventHandler<VoiceStateChangedEventArgs>? ListeningStopped;

    /// <summary>Raised for each interim hypothesis while the user is still speaking.</summary>
    event EventHandler<VoiceTranscriptEventArgs>? PartialTranscriptReceived;

    /// <summary>Raised once with the settled transcript for the utterance.</summary>
    event EventHandler<VoiceTranscriptEventArgs>? FinalTranscriptReceived;

    /// <summary>Raised when recognition fails, for example on a denied microphone.</summary>
    event EventHandler<VoiceRecognitionFailedEventArgs>? RecognitionFailed;

    /// <summary>Starts a recognition session. Calling it while already listening succeeds without restarting.</summary>
    Task<Result> StartListeningAsync(
        SpeechRecognitionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Stops the session and keeps the transcript captured so far.</summary>
    Task<Result> StopListeningAsync(CancellationToken cancellationToken = default);

    /// <summary>Aborts the session and discards the transcript.</summary>
    Task<Result> CancelListeningAsync(CancellationToken cancellationToken = default);
}
