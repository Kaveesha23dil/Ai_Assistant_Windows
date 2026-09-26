using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models;

/// <summary>
/// An application the resolver is willing to launch.
/// <para>
/// The resolver is what makes application launching safe: the voice layer only ever supplies
/// a human name, and only a resolved target reaches the shell. There is deliberately no
/// member that accepts a raw command line.
/// </para>
/// </summary>
public sealed record ApplicationTarget
{
    public ApplicationTarget(string name, string target, ApplicationTargetKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        Name = name;
        Target = target;
        Kind = kind;
    }

    /// <summary>Gets the canonical display name of the application.</summary>
    public string Name { get; }

    /// <summary>Gets the launch target: a path, URI, or application user model ID.</summary>
    public string Target { get; }

    /// <summary>Gets how <see cref="Target"/> must be launched.</summary>
    public ApplicationTargetKind Kind { get; }
}
