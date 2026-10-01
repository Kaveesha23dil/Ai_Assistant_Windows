using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Agents;

/// <summary>
/// What the agent is doing right now, reported so an interface can follow along.
/// <para>
/// The rules on this type are the same as the rules on the activity record, and they exist for
/// the same reason: this object is the most convenient thing in the whole system for a tool to
/// put a document's text into, because it is already on its way to the screen. It therefore
/// carries a step number, a tool name, a status, a sentence, and the plan and the approval being
/// worked on — and nothing that came out of a document, a screen, or a provider.
/// </para>
/// <para>
/// The plan and the step are carried whole rather than flattened to their names, and that is a
/// deliberate widening of what earlier drafts of this type held. A workspace cannot show a plan
/// from a count and a tool name; it has to draw the steps, and it cannot answer a prompt it was
/// never given the identifier for. Both are safe to carry — the plan holds descriptions and
/// parameters, the step holds an outcome, and neither holds a step's content — and withholding
/// them bought a smaller type at the cost of a page that could not function.
/// </para>
/// <para>
/// The approval is carried for the same reason and is the reason a caller does not have to ask
/// the agent for the pending request a second time. It carries the action, its description, its
/// risk, and the switch it needs, which is exactly what the prompt shows and nothing more.
/// </para>
/// </summary>
public sealed record AgentProgress
{
    private AgentProgress(
        Guid runId,
        AgentProgressKind kind,
        int stepOrder,
        string stepDescription,
        string toolName,
        AgentStepStatus status,
        string? message,
        int completedSteps,
        int totalSteps,
        AgentStep? step = null,
        AgentPlan? plan = null,
        AgentApprovalRequest? approval = null)
    {
        RunId = runId;
        Kind = kind;
        StepOrder = stepOrder;
        StepDescription = stepDescription;
        ToolName = toolName;
        Status = status;
        Message = message;
        CompletedSteps = completedSteps;
        TotalSteps = totalSteps;
        Step = step;
        Plan = plan;
        Approval = approval;
    }

    /// <summary>Gets the run this progress belongs to.</summary>
    public Guid RunId { get; }

    /// <summary>Gets what this report is about.</summary>
    public AgentProgressKind Kind { get; }

    /// <summary>Gets which step is being reported, counted from one.</summary>
    public int StepOrder { get; }

    /// <summary>Gets what the step is for, phrased for a person.</summary>
    public string StepDescription { get; }

    /// <summary>Gets the tool the step calls.</summary>
    public string ToolName { get; }

    /// <summary>Gets what has happened to the step.</summary>
    public AgentStepStatus Status { get; }

    /// <summary>Gets the sentence to show, or <see langword="null"/> when there is nothing to add.</summary>
    public string? Message { get; }

    /// <summary>Gets how many steps have finished.</summary>
    public int CompletedSteps { get; }

    /// <summary>Gets how many steps the plan has.</summary>
    public int TotalSteps { get; }

    /// <summary>Gets the step itself, or <see langword="null"/> for a report about the run.</summary>
    public AgentStep? Step { get; }

    /// <summary>Gets the plan, when the report is about the plan or the run as a whole.</summary>
    public AgentPlan? Plan { get; }

    /// <summary>Gets the approval the run is waiting on, when it is waiting on one.</summary>
    public AgentApprovalRequest? Approval { get; }

    /// <summary>Gets a value indicating whether the run has reached the end of its plan.</summary>
    public bool IsFinished => Kind == AgentProgressKind.Completed;

    /// <summary>Reports that a plan has been built and checked, and is about to be run.</summary>
    public static AgentProgress PlanReady(Guid runId, AgentPlan plan, int totalSteps)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new AgentProgress(
            runId,
            AgentProgressKind.PlanReady,
            0,
            plan.Goal,
            "agent",
            AgentStepStatus.Running,
            null,
            0,
            totalSteps,
            plan: plan);
    }

    /// <summary>Reports that a step has started.</summary>
    public static AgentProgress Started(
        Guid runId,
        AgentStep step,
        int completedSteps,
        int totalSteps)
    {
        ArgumentNullException.ThrowIfNull(step);
        return new AgentProgress(
            runId,
            AgentProgressKind.StepStarted,
            step.Order,
            step.Description,
            step.ToolName,
            AgentStepStatus.Running,
            null,
            completedSteps,
            totalSteps,
            step: step);
    }

    /// <summary>
    /// Reports that a step has finished, with a sentence taken from the step's own summary. That
    /// summary is written by the tool from counts and names, so it is safe to show; the step's
    /// content is not passed here and cannot be.
    /// </summary>
    public static AgentProgress Finished(
        Guid runId,
        AgentStep step,
        int completedSteps,
        int totalSteps)
    {
        ArgumentNullException.ThrowIfNull(step);
        return new AgentProgress(
            runId,
            AgentProgressKind.StepFinished,
            step.Order,
            step.Description,
            step.ToolName,
            step.Status,
            step.ResultSummary ?? step.ErrorMessage,
            completedSteps,
            totalSteps,
            step: step);
    }

    /// <summary>
    /// Reports that the run is waiting for a person to answer something, carrying the request so
    /// the caller can show it and answer it without going back to the agent.
    /// </summary>
    public static AgentProgress AwaitingApproval(
        Guid runId,
        AgentStep step,
        AgentApprovalRequest approval,
        int completedSteps,
        int totalSteps)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(approval);

        return new AgentProgress(
            runId,
            AgentProgressKind.AwaitingApproval,
            step.Order,
            step.Description,
            step.ToolName,
            AgentStepStatus.AwaitingApproval,
            approval.Description,
            completedSteps,
            totalSteps,
            step: step,
            approval: approval);
    }

    /// <summary>Reports that the run has ended, one way or another.</summary>
    public static AgentProgress Completed(
        Guid runId,
        AgentPlan plan,
        AgentPlanStatus status,
        string? message = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new AgentProgress(
            runId,
            AgentProgressKind.Completed,
            plan.Steps.Count,
            plan.Goal,
            "agent",
            status == AgentPlanStatus.Completed ? AgentStepStatus.Succeeded : AgentStepStatus.Failed,
            message,
            plan.Steps.Count,
            plan.Steps.Count,
            plan: plan);
    }
}
