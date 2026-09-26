namespace WindowsAIAssistant.Core.Exceptions;

/// <summary>
/// Base class for expected voice-assistant failures. Carries a stable error code so callers
/// can branch on the failure without matching on message text.
/// </summary>
public class VoiceException : AssistantException
{
    public VoiceException(string errorCode, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        ErrorCode = errorCode;
    }

    public VoiceException(string errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        ErrorCode = errorCode;
    }

    /// <summary>Gets the stable error code for this failure.</summary>
    public string ErrorCode { get; }
}
