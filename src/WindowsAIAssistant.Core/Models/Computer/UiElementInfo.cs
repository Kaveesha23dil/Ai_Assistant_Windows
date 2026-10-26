namespace WindowsAIAssistant.Core.Models.Computer;

public sealed record UiElementInfo
{
    public required string Id { get; init; }

    public string? Name { get; init; }

    public string? AutomationId { get; init; }

    public string? ControlType { get; init; }

    public string? ClassName { get; init; }

    public string? Value { get; init; }

    public bool IsEnabled { get; init; }

    public bool IsOffscreen { get; init; }

    public bool IsKeyboardFocusable { get; init; }

    public bool SupportsInvoke { get; init; }

    public bool SupportsValuePattern { get; init; }

    public bool SupportsSelectionItem { get; init; }

    public ComputerRect Bounds { get; init; }

    public long WindowHandle { get; init; }

    public int Depth { get; init; }

    public IReadOnlyList<UiElementInfo> Children { get; init; } = [];

    public string Label => Name ?? AutomationId ?? ControlType ?? Id;
}
