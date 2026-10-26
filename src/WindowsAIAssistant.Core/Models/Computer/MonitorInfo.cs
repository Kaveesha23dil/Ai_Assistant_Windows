namespace WindowsAIAssistant.Core.Models.Computer;

public sealed record MonitorInfo
{
    public required int Index { get; init; }

    public required string Name { get; init; }

    public required ComputerRect Bounds { get; init; }

    public bool IsPrimary { get; init; }

    public double DpiScale { get; init; } = 1.0;
}
