namespace WindowsAIAssistant.Core.Models.Computer;

public sealed record WindowInfo
{
    public required long Handle { get; init; }

    public required string Title { get; init; }

    public int ProcessId { get; init; }

    public string? ProcessName { get; init; }

    public bool IsVisible { get; init; }

    public bool IsForeground { get; init; }

    public ComputerRect Bounds { get; init; }

    public int MonitorIndex { get; init; }

    public string Label => string.IsNullOrWhiteSpace(Title)
        ? ProcessName ?? $"Window {Handle}"
        : Title;
}
