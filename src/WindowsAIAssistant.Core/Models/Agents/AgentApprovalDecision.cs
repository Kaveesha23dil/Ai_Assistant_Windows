using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Agents;

/// <summary>
/// How a person answered an approval request.
/// <para>
/// The modification, when there is one, is kept as text rather than as a patch applied to the
/// plan. The executor interprets it against the step that asked, which is the only layer that
/// knows what the step's parameters mean — so a request to "put it in Downloads instead" is
/// understood by the report tool, and an answer that cannot be understood is reported as
/// refused rather than quietly ignored.
/// </para>
/// </summary>
public sealed record AgentApprovalDecision
{
    public AgentApprovalDecision(
        Guid approvalId,
        AgentApprovalOutcome outcome,
        DateTimeOffset decidedAt,
        string? modification = null,
        string? responseText = null)
    {
        Id = approvalId;
        Outcome = outcome;
        DecidedAt = decidedAt;
        Modification = string.IsNullOrWhiteSpace(modification) ? null : modification.Trim();
        ResponseText = responseText?.Trim() ?? DefaultText(outcome);
    }

    /// <summary>Gets the identifier of the request being answered.</summary>
    public Guid Id { get; }

    /// <summary>Gets what the person chose.</summary>
    public AgentApprovalOutcome Outcome { get; }

    /// <summary>Gets when they answered.</summary>
    public DateTimeOffset DecidedAt { get; }

    /// <summary>
    /// Gets what they asked to change, when they chose <see cref="AgentApprovalOutcome.Modified"/>.
    /// Null for every other outcome, so there is never an instruction attached to a step that
    /// was simply approved.
    /// </summary>
    public string? Modification { get; }

    /// <summary>Gets the sentence shown afterwards, saying what the assistant took them to mean.</summary>
    public string ResponseText { get; }

    /// <summary>Gets a value indicating whether the step may run.</summary>
    public bool AllowsExecution =>
        Outcome is AgentApprovalOutcome.Approved or AgentApprovalOutcome.Modified;

    /// <summary>Records an approval with no changes.</summary>
    public static AgentApprovalDecision Approve(Guid approvalId) =>
        new(approvalId, AgentApprovalOutcome.Approved, DateTimeOffset.UtcNow);

    /// <summary>Records a refusal.</summary>
    public static AgentApprovalDecision Reject(Guid approvalId) =>
        new(approvalId, AgentApprovalOutcome.Rejected, DateTimeOffset.UtcNow);

    /// <summary>
    /// Records a refusal with a reason. The reason is kept because "no, don't touch that folder"
    /// is worth remembering for the rest of the conversation, and it is a statement about the
    /// person's wishes rather than about their data.
    /// </summary>
    public static AgentApprovalDecision Reject(Guid approvalId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new AgentApprovalDecision(
            approvalId,
            AgentApprovalOutcome.Rejected,
            DateTimeOffset.UtcNow,
            modification: null,
            responseText: $"Understood — I have not done that. {reason.Trim()}");
    }

    /// <summary>Records an approval with a change attached.</summary>
    public static AgentApprovalDecision Modify(Guid approvalId, string modification)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modification);

        return new AgentApprovalDecision(
            approvalId,
            AgentApprovalOutcome.Modified,
            DateTimeOffset.UtcNow,
            modification,
            $"Applying your change: {modification.Trim()}");
    }

    /// <summary>
    /// Records that nobody answered. Treated exactly as a refusal, because a step that ran
    /// because a prompt went unread is a step that ran without permission.
    /// </summary>
    public static AgentApprovalDecision TimedOut(Guid approvalId) =>
        new(
            approvalId,
            AgentApprovalOutcome.TimedOut,
            DateTimeOffset.UtcNow,
            modification: null,
            responseText: "No answer was given, so I did not go ahead.");

    /// <summary>
    /// Records that the run was stopped while the prompt was still up. Kept apart from a refusal
    /// and from a timeout, because a person who pressed stop did not decline the action — they
    /// abandoned the request, and the sentence for that is not "I have not done that".
    /// <para>
    /// It also never allows execution, so a cancelled run cannot be resumed by answering a
    /// prompt that outlived it.
    /// </para>
    /// </summary>
    public static AgentApprovalDecision Cancelled(Guid approvalId) =>
        new(
            approvalId,
            AgentApprovalOutcome.Cancelled,
            DateTimeOffset.UtcNow,
            modification: null,
            responseText: "The request was stopped, so I did not go ahead.");

    private static string DefaultText(AgentApprovalOutcome outcome) => outcome switch
    {
        AgentApprovalOutcome.Approved => "Approved.",
        AgentApprovalOutcome.Rejected => "I have not done that.",
        AgentApprovalOutcome.Modified => "Applying your change.",
        AgentApprovalOutcome.TimedOut => "No answer was given, so I did not go ahead.",
        AgentApprovalOutcome.Cancelled => "The request was stopped, so I did not go ahead.",
        _ => "No answer was given, so I did not go ahead.",
    };
}
