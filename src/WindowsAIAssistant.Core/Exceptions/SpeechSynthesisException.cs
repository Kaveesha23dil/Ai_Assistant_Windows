namespace WindowsAIAssistant.Core.Exceptions;

/// <summary>Raised when spoken output cannot be produced or cannot be stopped.</summary>
public class SpeechSynthesisException : VoiceException
{
    public SpeechSynthesisException(string errorCode, string message)
        : base(errorCode, message)
    {
    }

    public SpeechSynthesisException(string errorCode, string message, Exception innerException)
        : base(errorCode, message, innerException)
    {
    }
}
