namespace WindowsAIAssistant.Application.Common.Errors;

/// <summary>
/// A user-safe representation of a failure. It intentionally carries no stack trace,
/// exception type, file path, or configuration value so it can be surfaced to the UI.
/// </summary>
public sealed record ApplicationError
{
    public ApplicationError(
        string code,
        string message,
        ErrorType type,
        bool isRetryable = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Code = code;
        Message = message;
        Type = type;
        IsRetryable = isRetryable;
    }

    /// <summary>Gets the stable error code, for example <c>AI_REQUEST_FAILED</c>.</summary>
    public string Code { get; }

    /// <summary>Gets the safe message intended for display to a user.</summary>
    public string Message { get; }

    /// <summary>Gets the failure classification.</summary>
    public ErrorType Type { get; }

    /// <summary>
    /// Gets a value indicating whether retrying the same operation could succeed.
    /// Retry policies themselves belong to a later Infrastructure step.
    /// </summary>
    public bool IsRetryable { get; }
}
