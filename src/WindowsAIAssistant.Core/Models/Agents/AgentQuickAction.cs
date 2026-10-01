using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Agents;

/// <summary>
/// One of the named starting points the workspace offers, so that somebody looking at the page
/// for the first time has something to press.
/// <para>
/// A label, a prompt, and the capability it needs — nothing else. In particular there is no tool
/// name here and no expected outcome, because a quick action is not a demonstration: it has no
/// claim to check and it is not presented as one. Pressing one fills the request box with the
/// prompt and runs it, so what happens next is decided by the planner exactly as it would be if
/// the same words had been typed.
/// </para>
/// <para>
/// The capability is carried so the interface can disable the button and say why, rather than
/// offering something that will be refused a second later. It names a capability rather than a
/// tool so that turning a switch off is visible on the page before it is pressed.
/// </para>
/// </summary>
public sealed record AgentQuickAction(
    string Title,
    string Prompt,
    AgentCapability Capability)
{
    /// <summary>Gets the sentence explaining why the action is not available right now.</summary>
    /// <remarks>
    /// Written by the interface rather than carried here, because the reason belongs to the
    /// permission service and would otherwise be a second copy of its wording that could drift.
    /// </remarks>
    public string? UnavailableReason { get; init; }

    /// <summary>Gets a value indicating whether this action can be started right now.</summary>
    public bool IsAvailable => string.IsNullOrWhiteSpace(UnavailableReason);

    /// <summary>Gets the request this action produces, tagged as a quick action.</summary>
    public AgentRequestContext AsContext() => AgentRequestContext.QuickAction(this);
}