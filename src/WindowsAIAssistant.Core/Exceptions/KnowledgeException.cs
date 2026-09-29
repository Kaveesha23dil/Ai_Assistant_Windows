using WindowsAIAssistant.Core.Common;

namespace WindowsAIAssistant.Core.Exceptions;

/// <summary>
/// A knowledge-base operation failed for a reason a person can act on.
/// <para>
/// The code names the situation rather than the layer that noticed it, so the same failure reads
/// the same whether it came from reading a file, from a provider, or from arithmetic on two
/// vectors. Callers branch on <see cref="ErrorCode"/> to decide between "tell them", "ask for a
/// reindex", and "ask for permission", which is why each code below exists as its own constant
/// rather than one generic failure.
/// </para>
/// <para>
/// Like every message in this application, the text is safe to show and safe to log: no
/// document text, no question, no chunk, and no local path.
/// </para>
/// </summary>
public class KnowledgeException : AssistantException
{
    public KnowledgeException()
    {
    }

    public KnowledgeException(string message)
        : base(message)
    {
    }

    public KnowledgeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public KnowledgeException(string message, string errorCode)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public KnowledgeException(string message, string errorCode, Exception? innerException)
        : base(message, innerException!)
    {
        ErrorCode = errorCode;
    }

    /// <summary>Gets the stable code describing what went wrong.</summary>
    public string ErrorCode { get; } = ErrorCodes.KnowledgeOperationFailed;
}
