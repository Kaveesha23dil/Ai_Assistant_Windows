namespace WindowsAIAssistant.Core.Models.Computer;

public sealed record KeyboardInput
{
    public string? Key { get; init; }

    public IReadOnlyList<string> Keys { get; init; } = [];

    public int DelayMilliseconds { get; init; } = 10;

    public bool IsShortcut => Keys.Count > 1;
}
