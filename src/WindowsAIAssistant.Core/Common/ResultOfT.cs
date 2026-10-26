namespace WindowsAIAssistant.Core.Common;

/// <summary>
/// Represents the outcome of an operation that produces a value.
/// </summary>
/// <typeparam name="T">The type of the value produced by the operation.</typeparam>
public sealed class Result<T> : Result
{
    private Result(
        bool isSuccess,
        string? errorMessage,
        T? value,
        string? errorCode = null,
        TimeSpan? duration = null)
        : base(isSuccess, errorMessage, errorCode)
    {
        Value = value;
        Duration = duration;
    }

    /// <summary>Gets the result value when the operation succeeded; default otherwise.</summary>
    public T? Value { get; }

    /// <summary>
    /// Gets how long the attempt took, when the caller measured it. A failure that timed out is
    /// otherwise indistinguishable from one that was refused immediately.
    /// </summary>
    public TimeSpan? Duration { get; }

    /// <summary>Creates a successful result containing the specified value.</summary>
    public static Result<T> Success(T value) => new(true, null, value);

    /// <summary>Creates a failed result with the specified error message.</summary>
    public new static Result<T> Failure(string errorMessage) => new(false, errorMessage, default);

    /// <summary>Creates a failed result with a stable code and the message to show.</summary>
    public new static Result<T> Failure(string errorCode, string errorMessage) =>
        new(false, errorMessage, default, errorCode);

    /// <summary>Creates a failed result that also carries how long the attempt took.</summary>
    public static Result<T> Failure(string errorCode, string errorMessage, TimeSpan duration) =>
        new(false, errorMessage, default, errorCode, duration);
}