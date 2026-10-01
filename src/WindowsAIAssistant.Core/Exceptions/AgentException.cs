using WindowsAIAssistant.Core.Common;

namespace WindowsAIAssistant.Core.Exceptions;

/// <summary>
/// An agent operation failed for a reason a person can act on.
/// <para>
/// Its own type rather than a reuse of the knowledge one, because the codes it carries are about
/// the agent's own state — a step that failed, a store that will not open, a memory that was
/// refused — and a caller branching on <see cref="ErrorCode"/> should not have to know which
/// feature produced the code to handle it.
/// </para>
/// <para>
/// Like every message in this application, the text is safe to show and safe to log: no document
/// text, no screen content, no question somebody asked, and no local path.
/// </para>
/// </summary>
public class AgentException : AssistantException
{
    public AgentException()
    {
    }

    public AgentException(string message)
        : base(message)
    {
    }

    public AgentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public AgentException(string message, string errorCode)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public AgentException(string message, string errorCode, Exception? innerException)
        : base(message, innerException!)
    {
        ErrorCode = errorCode;
    }

    /// <summary>Gets the stable code describing what went wrong.</summary>
    public string ErrorCode { get; } = ErrorCodes.AgentStepFailed;
}