using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Abstractions.Agents;

/// <summary>
/// Reports the presentation mode currently in effect.
/// <para>
/// The workspace's view models live in the pages and read a mode from configuration, but they are
/// also constructed in tests without a container. This is the same shape as
/// <c>IAIRequestDefaults</c> and for the same reason: the Application layer owns the policy — it
/// builds the presentation and decides what each mode means — while Infrastructure owns the
/// numbers, and neither should have to know about the other's types.
/// </para>
/// <para>
/// The interface exposes one value. It cannot expose a tool list, a consent switch, or an answer,
/// so nothing a host binds to this can make the assistant more capable than the machine it is on.
/// </para>
/// </summary>
public interface IAgentPresentationSettings
{
    /// <summary>Gets the mode the workspace is presenting itself in.</summary>
    AgentPresentationMode Mode { get; }
}
