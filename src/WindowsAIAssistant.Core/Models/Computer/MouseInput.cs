using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Computer;

public sealed record MouseInput
{
    public int? X { get; init; }

    public int? Y { get; init; }

    public MouseButtonType Button { get; init; } = MouseButtonType.Left;

    public bool DoubleClick { get; init; }

    public bool IsCoordinateClick => X.HasValue && Y.HasValue;
}
