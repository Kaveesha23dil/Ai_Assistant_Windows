namespace WindowsAIAssistant.Core.Common;

/// <summary>
/// Represents the outcome of an operation without relying on exceptions for control flow.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, string? errorMessage)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
    }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Gets a value indicating whether the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Gets the error message when the operation failed; otherwise <see langword="null"/>.</summary>
    public string? ErrorMessage { get; }

    /// <summary>Creates a successful result.</summary>
    public static Result Success() => new(true, null);

    /// <summary>Creates a failed result with the specified error message.</summary>
    public static Result Failure(string errorMessage) => new(false, errorMessage);
}