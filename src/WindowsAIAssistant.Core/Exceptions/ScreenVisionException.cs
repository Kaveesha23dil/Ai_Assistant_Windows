using WindowsAIAssistant.Core.Common;

namespace WindowsAIAssistant.Core.Exceptions;

/// <summary>
/// A screen-vision operation failed for a reason a person can act on.
/// <para>
/// Sits alongside <see cref="KnowledgeException"/> and <see cref="DocumentException"/> for the
/// same reason: the caller has to branch on a code to decide between "tell them", "ask for
/// permission", and "this machine cannot do that", and a bare message does not distinguish
/// those. Cancellation is not reported through here — a cancelled capture returns to idle
/// rather than raising, so a person who pressed Escape is never told something went wrong.
/// </para>
/// <para>
/// Every message is safe to show and safe to log: no pixels, no recognised text, no prompt, and
/// no local path.
/// </para>
/// </summary>
public class ScreenVisionException : AssistantException
{
    public ScreenVisionException()
    {
    }

    public ScreenVisionException(string message)
        : base(message)
    {
    }

    public ScreenVisionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ScreenVisionException(string message, string errorCode)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public ScreenVisionException(string message, string errorCode, Exception? innerException)
        : base(message, innerException!)
    {
        ErrorCode = errorCode;
    }

    /// <summary>Gets the stable code describing what went wrong.</summary>
    public string ErrorCode { get; } = ErrorCodes.ScreenCaptureFailed;
}
