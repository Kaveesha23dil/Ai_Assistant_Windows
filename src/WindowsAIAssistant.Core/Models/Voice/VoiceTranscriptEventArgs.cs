namespace WindowsAIAssistant.Core.Models.Voice;

/// <summary>Raised when a partial or final transcript becomes available.</summary>
public sealed class VoiceTranscriptEventArgs : EventArgs
{
    public VoiceTranscriptEventArgs(VoiceRecognitionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        Result = result;
    }

    /// <summary>Gets the recognition result, including whether it is final.</summary>
    public VoiceRecognitionResult Result { get; }

    /// <summary>Gets a value indicating whether this transcript is the final one.</summary>
    public bool IsFinal => Result.IsFinal;
}
