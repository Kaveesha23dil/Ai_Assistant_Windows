namespace WindowsAIAssistant.Core.Models.Computer;

public sealed record ComputerActionRequest
{
    public required ComputerAction Action { get; init; }

    public string? Purpose { get; init; }

    public bool IsConfirmed { get; init; }

    public bool IsSafeMode { get; init; } = true;

    public DateTimeOffset RequestedAt { get; init; } = DateTimeOffset.UtcNow;
}
