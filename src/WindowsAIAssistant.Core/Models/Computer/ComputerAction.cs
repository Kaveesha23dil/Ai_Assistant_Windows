using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Computer;

public sealed record ComputerAction
{
    public required ComputerActionType Type { get; init; }

    public ComputerTarget? Target { get; init; }

    public string? Text { get; init; }

    public string? Key { get; init; }

    public IReadOnlyList<string> Keys { get; init; } = [];

    public MouseButtonType MouseButton { get; init; } = MouseButtonType.Left;

    public int? X { get; init; }

    public int? Y { get; init; }

    public int ScrollDelta { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    public bool VerifyResult { get; init; } = true;

    public int MaxRetries { get; init; } = 2;

    public bool UseCoordinateFallback { get; init; }

    public bool HasCoordinates => X.HasValue && Y.HasValue;
}
