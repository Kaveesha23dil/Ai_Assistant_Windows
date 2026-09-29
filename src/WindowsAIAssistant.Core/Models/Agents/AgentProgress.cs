using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Core.Models.Agents;

/// <summary>
/// What the agent is doing right now, reported so an interface can follow along.
/// <para>
/// The rules on this type are the same as the rules on the activity record, and they exist for
/// the same reason: this object is the most convenient thing in the whole system for a tool to
/// put a document's text into, because it is already on its way to the screen. It therefore
/// carries a step number, a tool name, a status, and a sentence — and nothing that came out of
/// a document, a screen, or a provider.
/// </para>
/// </summary>
public sealed record AgentProgress
{
    private AgentProgress(
        Guid runId,
        int stepOrder,
        string stepDescription,
        string toolName,
        AgentStepStatus status,
        string? message,
        int completedSteps,
        int totalSteps)
    {
        RunId = runId;
        StepOrder = stepOrder;
        StepDescription = stepDescription;
        ToolName = toolName;
        Status = status;
        Message = message;
        CompletedSteps = completedSteps;
        TotalSteps = totalSteps;
    }

    /// <summary>Gets the run this progress belongs to.</summary>
    public Guid RunId { get; }

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

    /// <summary>Gets a value indicating whether the run has reached the end of its plan.</summary>
    public bool IsFinished => Status is AgentStepStatus.Succeeded
        or AgentStepStatus.Failed
        or AgentStepStatus.Rejected
        or AgentStepStatus.Skipped;

    /// <summary>Reports that a step has started.</summary>
    public static AgentProgress Started(
        Guid runId,
        AgentStep step,
        int completedSteps,
        int totalSteps)
    {
        ArgumentNullException.ThrowIfNull(step);
        return new AgentProgress(
            runId, step.Order, step.Description, step.ToolName, AgentStepStatus.Running, null, completedSteps, totalSteps);
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
            step.Order,
            step.Description,
            step.ToolName,
            step.Status,
            step.ResultSummary ?? step.ErrorMessage,
            completedSteps,
            totalSteps);
    }

    /// <summary>Reports that the run is waiting for a person to answer something.</summary>
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
            step.Order,
            step.Description,
            step.ToolName,
            AgentStepStatus.AwaitingApproval,
            approval.Description,
            completedSteps,
            totalSteps);
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
            plan.Steps.Count,
            plan.Goal,
            "agent",
            status == AgentPlanStatus.Completed ? AgentStepStatus.Succeeded : AgentStepStatus.Failed,
            message,
            plan.Steps.Count,
            plan.Steps.Count);
    }
}
