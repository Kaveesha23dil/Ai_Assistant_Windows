namespace WindowsAIAssistant.Core.Exceptions;

/// <summary>
/// Base exception for all expected Windows AI Assistant failures.
/// </summary>
public class AssistantException : Exception
{
    public AssistantException()
    {
    }

    public AssistantException(string message)
        : base(message)
    {
    }

    public AssistantException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}