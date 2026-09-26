namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// The explicit lifecycle state of the voice assistant.
/// <para>
/// A single enum replaces the scattered <c>isListening</c>, <c>isThinking</c>, and
/// <c>isSpeaking</c> booleans that would otherwise drift out of sync. The expected happy
/// path is <c>Idle</c> to <c>Listening</c> to <c>Processing</c> to <c>Executing</c> to
/// <c>Speaking</c> and back to <c>Idle</c>; any step may move to <c>Cancelled</c> or
/// <c>Error</c> before returning to <c>Idle</c>.
/// </para>
/// </summary>
public enum VoiceAssistantState
{
    /// <summary>The voice feature is switched off by configuration or privacy settings.</summary>
    Disabled,

    /// <summary>Ready to accept a new request.</summary>
    Idle,

    /// <summary>Capturing speech from the microphone.</summary>
    Listening,

    /// <summary>Converting a transcript into an intent.</summary>
    Processing,

    /// <summary>Running the handler selected for the recognized intent.</summary>
    Executing,

    /// <summary>Producing a spoken response.</summary>
    Speaking,

    /// <summary>The active operation was cancelled and the transcript discarded.</summary>
    Cancelled,

    /// <summary>The last operation failed. The next transition returns to <see cref="Idle"/>.</summary>
    Error
}
