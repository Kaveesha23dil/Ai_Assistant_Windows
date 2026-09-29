namespace WindowsAIAssistant.Core.Common;

/// <summary>
/// Represents the outcome of an operation that produces a value.
/// </summary>
/// <typeparam name="T">The type of the value produced by the operation.</typeparam>
public sealed class Result<T> : Result
{
    private Result(bool isSuccess, string? errorMessage, T? value, string? errorCode = null)
        : base(isSuccess, errorMessage, errorCode)
    {
        Value = value;
    }

    /// <summary>Gets the result value when the operation succeeded; default otherwise.</summary>
    public T? Value { get; }

    /// <summary>Creates a successful result containing the specified value.</summary>
    public static Result<T> Success(T value) => new(true, null, value);

    /// <summary>Creates a failed result with the specified error message.</summary>
    public new static Result<T> Failure(string errorMessage) => new(false, errorMessage, default);

    /// <summary>Creates a failed result with a stable code and the message to show.</summary>
    public new static Result<T> Failure(string errorCode, string errorMessage) =>
        new(false, errorMessage, default, errorCode);
}