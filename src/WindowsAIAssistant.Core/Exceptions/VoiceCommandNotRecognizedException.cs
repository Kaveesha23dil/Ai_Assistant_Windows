namespace WindowsAIAssistant.Core.Exceptions;

/// <summary>Raised when a transcript cannot be mapped to any supported intent.</summary>
public class VoiceCommandNotRecognizedException : VoiceException
{
    public VoiceCommandNotRecognizedException(string errorCode, string message)
        : base(errorCode, message)
    {
    }
}
