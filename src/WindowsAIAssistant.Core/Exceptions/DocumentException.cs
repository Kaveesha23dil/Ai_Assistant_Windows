namespace WindowsAIAssistant.Core.Exceptions;

/// <summary>
/// A document could not be read, or could not be analysed.
/// <para>
/// The code is the part callers branch on; the message is the part a person sees or hears, so
/// it is written to be safe to show. Neither should ever carry a file path, a library name, or
/// the contents of the document: this exception is logged, and a logged document problem must
/// not become a recorded copy of the file or its name.
/// </para>
/// </summary>
public class DocumentException : AssistantException
{
    /// <summary>Creates an exception with no message.</summary>
    public DocumentException()
    {
    }

    /// <summary>Creates an exception carrying a message.</summary>
    public DocumentException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception carrying a message and the failure underneath it.</summary>
    public DocumentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception carrying a message and a stable code.</summary>
    public DocumentException(string message, string errorCode)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Creates an exception carrying a message, a stable code, and the failure underneath it.
    /// The inner exception is kept for diagnostics and is never read into the message.
    /// </summary>
    public DocumentException(string message, string errorCode, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    /// <summary>Gets the stable code describing what went wrong.</summary>
    public string ErrorCode { get; } = Common.ErrorCodes.DocumentExtractionFailed;
}
