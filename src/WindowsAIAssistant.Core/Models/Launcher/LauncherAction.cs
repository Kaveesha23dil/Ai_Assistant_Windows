using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Launcher;

/// <summary>
/// The action a launcher result performs when it is chosen.
/// </summary>
public sealed record LauncherAction
{
    /// <summary>Gets what kind of action this is.</summary>
    public LauncherActionKind Kind { get; init; } = LauncherActionKind.None;

    /// <summary>
    /// Gets the action payload. Its meaning depends on <see cref="Kind"/>: an application name
    /// for a launch, a path or URI to open, a route name, or text to copy.
    /// </summary>
    public string? Target { get; init; }

    /// <summary>Creates the empty action used by non-actionable results.</summary>
    public static LauncherAction None => new();

    /// <summary>Creates an action of the given kind.</summary>
    public static LauncherAction For(LauncherActionKind kind, string? target = null) =>
        new() { Kind = kind, Target = target };
}
