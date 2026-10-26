using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Computer;

public sealed record ComputerActionResult
{
    public required ComputerActionType ActionType { get; init; }

    public required bool Succeeded { get; init; }

    public string Message { get; init; } = string.Empty;

    public string? ErrorCode { get; init; }

    public string? Verification { get; init; }

    public WindowInfo? ActiveWindow { get; init; }

    public UiElementInfo? Element { get; init; }

    public IReadOnlyDictionary<string, string> Data { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public TimeSpan Duration { get; init; }

    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.UtcNow;

    public static ComputerActionResult Success(
        ComputerActionType actionType,
        string message,
        TimeSpan duration = default,
        WindowInfo? activeWindow = null,
        UiElementInfo? element = null,
        IReadOnlyDictionary<string, string>? data = null,
        string? verification = null) =>
        new()
        {
            ActionType = actionType,
            Succeeded = true,
            Message = message,
            Duration = duration,
            ActiveWindow = activeWindow,
            Element = element,
            Data = data ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Verification = verification
        };

    public static ComputerActionResult Failure(
        ComputerActionType actionType,
        string errorCode,
        string message,
        TimeSpan duration = default) =>
        new()
        {
            ActionType = actionType,
            Succeeded = false,
            ErrorCode = errorCode,
            Message = message,
            Duration = duration
        };
}
