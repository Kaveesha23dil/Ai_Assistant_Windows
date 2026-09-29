using System.Globalization;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Agents;

/// <summary>
/// A question put to a person before a step runs: what is about to happen, how risky it is, and
/// which consent switch it depends on.
/// <para>
/// This is the only object in the agent that exists to be answered. It is created before
/// anything happens, holds nothing that was read from a document or a screen, and is shown
/// verbatim — which is what lets the interface promise that approving an action is the same as
/// seeing it, and lets the timeline record that somebody was asked without recording anything
/// they had to look at to answer.
/// </para>
/// <para>
/// Every request carries a deadline. A plan that waits forever for an answer is a plan that
/// never finishes and never says why, so <see cref="TimesOutAt"/> exists to turn silence into a
/// refusal that the executor can act on.
/// </para>
/// </summary>
public sealed record AgentApprovalRequest
{
    public AgentApprovalRequest(
        Guid id,
        Guid runId,
        string action,
        string description,
        ActionRiskLevel riskLevel,
        int stepOrder,
        string stepDescription,
        PermissionCapability? requiredPermission = null,
        DateTimeOffset? timesOutAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(stepDescription);

        Id = id;
        RunId = runId;
        Action = action.Trim();
        Description = description.Trim();
        RiskLevel = riskLevel;
        StepOrder = stepOrder;
        StepDescription = stepDescription.Trim();
        RequiredPermission = requiredPermission;
        TimesOutAt = timesOutAt;
        RequestedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Gets the identifier an answer must quote back.</summary>
    public Guid Id { get; }

    /// <summary>Gets the run that is waiting, so a late answer to an abandoned run is recognisable.</summary>
    public Guid RunId { get; }

    /// <summary>Gets a short name for the action, shown as the heading of the prompt.</summary>
    public string Action { get; }

    /// <summary>
    /// Gets the sentence that says what will happen and, for anything that writes, where it will
    /// be written. This is the text a person is agreeing to, so it is never assembled from a
    /// tool name or a file path that they did not already know.
    /// </summary>
    public string Description { get; init; }

    /// <summary>Gets how much damage the action could do if the plan was wrong.</summary>
    public ActionRiskLevel RiskLevel { get; }

    /// <summary>Gets the consent switch the action depends on, when it depends on one.</summary>
    public PermissionCapability? RequiredPermission { get; }

    /// <summary>Gets which step of the plan is being asked about, counted from one.</summary>
    public int StepOrder { get; }

    /// <summary>Gets what the step is for, so the prompt has context beyond the action alone.</summary>
    public string StepDescription { get; }

    /// <summary>Gets when the prompt was created.</summary>
    public DateTimeOffset RequestedAt { get; }

    /// <summary>
    /// Gets when the prompt stops waiting, or <see langword="null"/> when the host decides. A
    /// timeout is treated exactly as a refusal rather than as a failure, because somebody who
    /// walked away has not agreed to anything.
    /// </summary>
    public DateTimeOffset? TimesOutAt { get; }

    /// <summary>Gets the heading the interface shows above the description.</summary>
    public string Title => $"Approve: {Action}";

    /// <summary>Gets the one-word risk label a person can judge the prompt by at a glance.</summary>
    public string RiskLabel => RiskLevel switch
    {
        ActionRiskLevel.Low => "Low risk",
        ActionRiskLevel.Medium => "Medium risk",
        ActionRiskLevel.High => "High risk",
        _ => "Unknown risk",
    };

    /// <summary>Creates the request for one action, with a sensible default deadline.</summary>
    public static AgentApprovalRequest For(
        Guid runId,
        AgentStep step,
        AgentAction action,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(action);

        // Only a step that is actually able to ask is given a deadline. A caller that manages its
        // own timeout passes none, and a prompt that appears to expire on a schedule the host
        // does not honour is worse than one with no deadline at all.
        return new AgentApprovalRequest(
            Guid.NewGuid(),
            runId,
            action.Action,
            action.Description,
            action.RiskLevel,
            step.Order,
            step.Description,
            action.RequiredPermission,
            timeout is null ? null : DateTimeOffset.UtcNow.Add(timeout.Value));
    }

    /// <summary>Returns this request with a modification folded into its description.</summary>
    /// <remarks>
    /// Used when a person answers "yes, but put it somewhere else". The step is rewritten by the
    /// caller; this only makes the resulting record say what was actually agreed to, so the
    /// timeline does not claim a different action from the one that ran.
    /// </remarks>
    public AgentApprovalRequest WithDescription(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        return this with { Description = description.Trim() };
    }

    /// <summary>Gets a one-line form for a log entry, carrying no description of the person.</summary>
    public override string ToString() =>
        $"Approval {Id} for step {StepOrder.ToString(CultureInfo.InvariantCulture)} ({RiskLevel})";
}
