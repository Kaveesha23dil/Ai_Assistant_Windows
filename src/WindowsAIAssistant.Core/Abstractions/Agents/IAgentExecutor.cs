using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Core.Abstractions.Agents;

/// <summary>
/// Checks a plan against the tools that exist, and runs it step by step.
/// <para>
/// Validation and execution are two methods on one interface rather than one, because they
/// answer two different questions and a caller is allowed to ask the first without the second.
/// The workspace shows a validated plan to a person and waits; the voice path runs one
/// immediately. Both need the same checks, and neither needs the other's behaviour.
/// </para>
/// <para>
/// The executor is the only component permitted to move a plan out of
/// <see cref="Core.Enums.AgentPlanStatus.Draft"/>. Every step is checked before it runs — that
/// the tool exists, that its required inputs are present, that the consent switches it needs are
/// on, and that a person has answered if it is going to change anything — and a step that fails
/// a check stops the plan rather than being quietly skipped.
/// </para>
/// </summary>
public interface IAgentExecutor
{
    /// <summary>
    /// Checks a plan without running any of it: every step names a real tool, supplies what that
    /// tool requires, and is available on this machine.
    /// </summary>
    /// <returns>
    /// The plan, ready to run, or a failure naming the first step that cannot be. The failure
    /// says which step and why in terms a person can act on.
    /// </returns>
    Task<Result<AgentPlan>> ValidateAsync(
        AgentPlan plan,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a plan from its first step, collecting what each produces and returning one answer.
    /// </summary>
    /// <remarks>
    /// A plan that is not ready is refused rather than validated and run in one step, so that
    /// there is no code path in which an unchecked plan reaches a tool.
    /// </remarks>
    Task<Result<AgentExecutionResult>> ExecuteAsync(
        AgentPlan plan,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised as each step starts, is held at an approval, and finishes.
    /// <para>
    /// On the executor rather than only on the agent, so a caller that drives the executor
    /// directly — a test, or a page that wants to show a plan it built itself — sees the same
    /// stream a caller going through <see cref="IAgent"/> does. Two progress paths would be two
    /// timelines, and one of them would be the one nobody tested.
    /// </para>
    /// </summary>
    event EventHandler<AgentProgress>? ProgressChanged;

    /// <summary>
    /// Raises the approval a step needs and waits for an answer.
    /// <para>
    /// A step that changes nothing never reaches this. A step that changes something always does,
    /// including in a plan whose other steps are read-only, because approval is about what the
    /// step does rather than about the plan it is in.
    /// </para>
    /// </summary>
    Task<AgentApprovalDecision> RequestApprovalAsync(
        AgentApprovalRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers an approval that is already outstanding. Called by the interface when a person
    /// presses Approve, Reject, or Modify.
    /// </summary>
    /// <returns><see langword="false"/> when the request has already been answered or has expired.</returns>
    bool TryResolveApproval(AgentApprovalDecision decision);
}
