namespace WindowsAIAssistant.Core.Exceptions;

/// <summary>Raised when speech recognition cannot start or fails mid-session.</summary>
public class SpeechRecognitionException : VoiceException
{
    public SpeechRecognitionException(string errorCode, string message)
        : base(errorCode, message)
    {
    }

    public SpeechRecognitionException(string errorCode, string message, Exception innerException)
        : base(errorCode, message, innerException)
    {
    }
}
