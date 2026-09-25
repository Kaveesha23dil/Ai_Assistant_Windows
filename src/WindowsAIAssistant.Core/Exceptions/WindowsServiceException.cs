namespace WindowsAIAssistant.Core.Exceptions;

/// <summary>
/// Represents a failure originating from a Windows-integration service.
/// </summary>
public class WindowsServiceException : AssistantException
{
    public WindowsServiceException()
    {
    }

    public WindowsServiceException(string message)
        : base(message)
    {
    }

    public WindowsServiceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}