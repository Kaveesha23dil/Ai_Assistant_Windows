namespace WindowsAIAssistant.Core.Models.Computer;

public sealed record ComputerContext
{
    public WindowInfo? ActiveWindow { get; init; }

    public IReadOnlyList<WindowInfo> Windows { get; init; } = [];

    public IReadOnlyList<MonitorInfo> Monitors { get; init; } = [];

    public int CursorX { get; init; }

    public int CursorY { get; init; }

    public bool IsSafeMode { get; init; } = true;

    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
}
