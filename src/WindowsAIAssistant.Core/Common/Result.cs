namespace WindowsAIAssistant.Core.Common;

/// <summary>
/// Represents the outcome of an operation without relying on exceptions for control flow.
/// </summary>
public class Result
{
    protected Result(
        bool isSuccess,
        string? errorMessage,
        string? errorCode = null,
        string? successMessage = null)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        ErrorCode = errorCode;
        SuccessMessage = successMessage;
    }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Gets a value indicating whether the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Gets the error message when the operation failed; otherwise <see langword="null"/>.</summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Gets a stable code naming what went wrong, when the caller supplied one.
    /// <para>
    /// A message says what to tell a person; a code says what to do next. Both are carried,
    /// because the layer holding a user interface is not the layer that knows whether the reason
    /// it was handed was a refusal, a missing capability, or a fault, and a caller that can only
    /// see the message has to guess. Codes come from <see cref="ErrorCodes"/> and are safe to log.
    /// </para>
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Gets a message worth telling the person about when the operation succeeded, for example
    /// that the change was saved but needs a restart to take effect. <see langword="null"/> when
    /// there is nothing to add beyond the fact of success.
    /// </summary>
    public string? SuccessMessage { get; }

    /// <summary>Creates a successful result.</summary>
    public static Result Success() => new(true, null);

    /// <summary>
    /// Creates a successful result that carries a message for the person who asked for the work,
    /// used when the outcome needs something said about it.
    /// </summary>
    public static Result Success(string successMessage) => new(true, null, successMessage: successMessage);

    /// <summary>Creates a failed result with the specified error message.</summary>
    public static Result Failure(string errorMessage) => new(false, errorMessage);

    /// <summary>Creates a failed result with a stable code and the message to show.</summary>
    public static Result Failure(string errorCode, string errorMessage) =>
        new(false, errorMessage, errorCode);
}