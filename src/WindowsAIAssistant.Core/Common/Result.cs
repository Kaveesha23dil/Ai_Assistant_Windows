namespace WindowsAIAssistant.Core.Common;

/// <summary>
/// Represents the outcome of an operation without relying on exceptions for control flow.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, string? errorMessage, string? successMessage = null)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        SuccessMessage = successMessage;
    }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Gets a value indicating whether the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Gets the error message when the operation failed; otherwise <see langword="null"/>.</summary>
    public string? ErrorMessage { get; }

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
    public static Result Success(string successMessage) => new(true, null, successMessage);

    /// <summary>Creates a failed result with the specified error message.</summary>
    public static Result Failure(string errorMessage) => new(false, errorMessage);
}