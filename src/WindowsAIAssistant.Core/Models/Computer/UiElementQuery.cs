namespace WindowsAIAssistant.Core.Models.Computer;

public sealed record UiElementQuery
{
    public string? WindowTitle { get; init; }

    public string? ProcessName { get; init; }

    public long? WindowHandle { get; init; }

    public string? Name { get; init; }

    public string? AutomationId { get; init; }

    public string? ControlType { get; init; }

    public string? ClassName { get; init; }

    public string? Value { get; init; }

    public int Index { get; init; }

    public int MaxDepth { get; init; } = 8;

    public bool IncludeOffscreen { get; init; }

    public bool RequireEnabled { get; init; } = true;

    public bool UseCoordinateFallback { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);
}
