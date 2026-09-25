namespace WindowsAIAssistant.Core.Exceptions;

/// <summary>
/// Represents a failure originating from an AI provider or the AI service layer.
/// </summary>
public class AIServiceException : AssistantException
{
    public AIServiceException()
    {
    }

    public AIServiceException(string message)
        : base(message)
    {
    }

    public AIServiceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}