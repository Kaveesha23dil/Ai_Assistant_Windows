namespace WindowsAIAssistant.Core.Models.Launcher;

/// <summary>
/// The outcome of one launcher search.
/// </summary>
public sealed record LauncherResult
{
    /// <summary>Gets a value indicating whether the search completed.</summary>
    public bool IsSuccess { get; init; } = true;

    /// <summary>Gets the intent the query was routed to.</summary>
    public LauncherIntent Intent { get; init; } = LauncherIntent.Universal(string.Empty);

    /// <summary>Gets the ranked results.</summary>
    public IReadOnlyList<LauncherItem> Items { get; init; } = [];

    /// <summary>Gets a sentence to show when there is something to say about the outcome.</summary>
    public string? Message { get; init; }

    /// <summary>Gets a stable error code when the search failed.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Creates a successful result.</summary>
    public static LauncherResult Success(LauncherIntent intent, IReadOnlyList<LauncherItem> items) =>
        new() { Intent = intent, Items = items };

    /// <summary>Creates a failed result.</summary>
    public static LauncherResult Failure(
        LauncherIntent intent,
        string errorCode,
        string message) =>
        new()
        {
            IsSuccess = false,
            Intent = intent,
            ErrorCode = errorCode,
            Message = message,
        };
}
