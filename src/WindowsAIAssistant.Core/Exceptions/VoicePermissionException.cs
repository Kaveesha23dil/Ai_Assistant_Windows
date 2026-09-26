namespace WindowsAIAssistant.Core.Exceptions;

/// <summary>Raised when an action is attempted without the required user consent.</summary>
public class VoicePermissionException : VoiceException
{
    public VoicePermissionException(string errorCode, string message)
        : base(errorCode, message)
    {
    }
}
