namespace WindowsAIAssistant.Core.Models.Voice;

/// <summary>Raised when the assistant produces a response the user can see and hear.</summary>
public sealed class VoiceResponseEventArgs : EventArgs
{
    public VoiceResponseEventArgs(VoiceCommandResult result, bool wasSpoken)
    {
        ArgumentNullException.ThrowIfNull(result);

        Result = result;
        WasSpoken = wasSpoken;
    }

    /// <summary>Gets the result that produced the response.</summary>
    public VoiceCommandResult Result { get; }

    /// <summary>
    /// Gets a value indicating whether the response was also read aloud. Spoken responses
    /// are truncated to the configured maximum length, so this may be false when the user
    /// has disabled spoken replies.
    /// </summary>
    public bool WasSpoken { get; }
}
