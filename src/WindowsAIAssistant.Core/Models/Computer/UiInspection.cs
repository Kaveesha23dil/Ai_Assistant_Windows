namespace WindowsAIAssistant.Core.Models.Computer;

public sealed record UiInspection
{
    public WindowInfo? Window { get; init; }

    public UiElementInfo? FocusedElement { get; init; }

    public IReadOnlyList<UiElementInfo> Elements { get; init; } = [];

    public bool Truncated { get; init; }

    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
}
