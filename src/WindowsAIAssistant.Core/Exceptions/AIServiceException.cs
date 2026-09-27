namespace WindowsAIAssistant.Core.Exceptions;

/// <summary>
/// Represents a failure originating from an AI provider or the AI service layer.
/// <para>
/// The code is what makes the message safe to show. A provider can fail for reasons that must
/// never reach a person verbatim — a rejected key, a rate limit, a timeout, an unreachable
/// host — so the layer that understands the vendor error replaces it with one of the shared
/// <see cref="Common.ErrorCodes"/> values and a sentence written for a user. Anything that does
/// not fit is reported as <see cref="Common.ErrorCodes.AiRequestFailed"/> rather than
/// described.
/// </para>
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

    public AIServiceException(string message, string errorCode)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public AIServiceException(string message, string errorCode, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Gets the stable, user-safe code for the failure. Defaults to
    /// <see cref="Common.ErrorCodes.AiRequestFailed"/>, so an unclassified failure still
    /// reports a code rather than nothing.
    /// </summary>
    public string? ErrorCode { get; } = Common.ErrorCodes.AiRequestFailed;
}
