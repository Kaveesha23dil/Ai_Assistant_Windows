using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Computer;

public sealed record ComputerTarget
{
    public ComputerTargetKind Kind { get; init; } = ComputerTargetKind.Element;

    public string? ApplicationName { get; init; }

    public string? WindowTitle { get; init; }

    public string? ProcessName { get; init; }

    public string? ElementName { get; init; }

    public string? AutomationId { get; init; }

    public string? ControlType { get; init; }

    public string? ClassName { get; init; }

    public int? X { get; init; }

    public int? Y { get; init; }

    public bool HasCoordinates => X.HasValue && Y.HasValue;

    public static ComputerTarget Application(string name) =>
        new() { Kind = ComputerTargetKind.Application, ApplicationName = name };

    public static ComputerTarget Window(string title, string? processName = null) =>
        new() { Kind = ComputerTargetKind.Window, WindowTitle = title, ProcessName = processName };

    public static ComputerTarget Element(string? name = null, string? automationId = null) =>
        new() { Kind = ComputerTargetKind.Element, ElementName = name, AutomationId = automationId };

    public static ComputerTarget Coordinates(int x, int y) =>
        new() { Kind = ComputerTargetKind.Coordinates, X = x, Y = y };
}
