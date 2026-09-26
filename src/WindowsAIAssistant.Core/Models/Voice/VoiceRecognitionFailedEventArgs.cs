namespace WindowsAIAssistant.Core.Models.Voice;

/// <summary>Describes a speech recognition failure without exposing provider detail.</summary>
public sealed class VoiceRecognitionFailedEventArgs : EventArgs
{
    public VoiceRecognitionFailedEventArgs(string errorCode, string message, bool isRecoverable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        ErrorCode = errorCode;
        Message = message;
        IsRecoverable = isRecoverable;
    }

    /// <summary>Gets the stable error code for the failure.</summary>
    public string ErrorCode { get; }

    /// <summary>Gets a user-safe description of the failure.</summary>
    public string Message { get; }

    /// <summary>Gets a value indicating whether listening can be retried.</summary>
    public bool IsRecoverable { get; }
}
