using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Agents;

/// <summary>
/// One action a tool has been asked to take, and the risk of allowing it.
/// <para>
/// This is the unit a person approves. A plan is approved as a whole, but an approval is always
/// given for something specific: "write this report to that file", not "run the plan". Keeping
/// the description, the risk level, and the permission on one small value is what lets the
/// interface say exactly what is about to happen before anything does.
/// </para>
/// <para>
/// The risk level is a property of the action, not of the tool that proposes it. The report tool
/// is <see cref="ActionRiskLevel.Low"/> while composing text and
/// <see cref="ActionRiskLevel.Medium"/> the moment it is about to write a file, because what
/// changes is whether a person's disk changes, not which class the code belongs to.
/// </para>
/// </summary>
public sealed record AgentAction
{
    public AgentAction(
        string action,
        string description,
        ActionRiskLevel riskLevel,
        PermissionCapability? requiredPermission = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Action = action.Trim();
        Description = description.Trim();
        RiskLevel = riskLevel;
        RequiredPermission = requiredPermission;
    }

    /// <summary>Gets a short name for the action, used in the timeline and in a log.</summary>
    public string Action { get; }

    /// <summary>
    /// Gets the sentence a person reads before deciding. It must name the thing that is about
    /// to change and, for anything that writes, where it is about to be written.
    /// </summary>
    public string Description { get; }

    /// <summary>Gets how much damage this action could do if the plan was wrong.</summary>
    public ActionRiskLevel RiskLevel { get; }

    /// <summary>
    /// Gets the consent switch this action needs, when it needs one. Null for an action that
    /// changes nothing and reaches nothing.
    /// </summary>
    public PermissionCapability? RequiredPermission { get; }

    /// <summary>Gets a value indicating whether a person must answer before this runs.</summary>
    public bool RequiresApproval => RiskLevel > ActionRiskLevel.Low;

    /// <summary>Creates a read-only action, which runs without asking.</summary>
    public static AgentAction Read(string action, string description) =>
        new(action, description, ActionRiskLevel.Low);

    /// <summary>
    /// Creates an action that writes something, which asks first. The permission is required
    /// rather than optional here because a write without a consent switch behind it is exactly
    /// the case this whole mechanism exists for.
    /// </summary>
    public static AgentAction Write(
        string action,
        string description,
        PermissionCapability requiredPermission) =>
        new(action, description, ActionRiskLevel.Medium, requiredPermission);

    /// <summary>Creates an action that could destroy something, which asks first and is rarer.</summary>
    public static AgentAction Destructive(
        string action,
        string description,
        PermissionCapability requiredPermission) =>
        new(action, description, ActionRiskLevel.High, requiredPermission);
}
