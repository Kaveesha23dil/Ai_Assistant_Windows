using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Agents;

/// <summary>
/// What the agent decided to do about a request, and where it got to.
/// <para>
/// A plan is a goal and an ordered list of steps. It is produced in
/// <see cref="AgentPlanStatus.Draft"/> and only becomes <see cref="AgentPlanStatus.Ready"/>
/// after every step has been checked against the tools that are actually installed and after
/// nothing in it has been refused for risk. There is no method that jumps from Draft to
/// Executing, and that is deliberate: the output of a planner is text produced by a model, and a
/// plan that could be executed without a check is a remote shell with extra steps.
/// </para>
/// <para>
/// The steps are read-only once a plan exists. Execution produces new plans rather than
/// mutating this one, which is what lets the plan a person approved be the plan that is on
/// screen afterwards, and what lets a half-finished run be described without having lost the
/// steps that never ran.
/// </para>
/// </summary>
public sealed record AgentPlan
{
    private readonly IReadOnlyList<AgentStep> _steps;

    public AgentPlan(
        Guid id,
        string goal,
        IEnumerable<AgentStep> steps,
        DateTimeOffset createdAt,
        string originalRequest,
        AgentRequestSource source = AgentRequestSource.Text,
        string? explanation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(goal);
        ArgumentNullException.ThrowIfNull(steps);

        var ordered = steps
            .OrderBy(step => step.Order)
            .ToArray();

        if (ordered.Length == 0)
        {
            throw new ArgumentException("A plan must have at least one step.", nameof(steps));
        }

        // Step numbers are the executor's index into this list, so a gap or a repeat would make
        // "which step is third" ambiguous. Re-numbering here means a planner that emitted
        // 0-based or duplicated numbers produces the same plan as one that did not.
        _steps = [.. ordered.Select((step, index) => step with { Order = index + 1 })];

        Id = id;
        Goal = goal.Trim();
        CreatedAt = createdAt;
        OriginalRequest = originalRequest?.Trim() ?? string.Empty;
        Source = source;
        Explanation = string.IsNullOrWhiteSpace(explanation) ? null : explanation.Trim();
    }

    /// <summary>Gets this plan's identifier, shared with the activity timeline.</summary>
    public Guid Id { get; }

    /// <summary>Gets the goal, in one sentence, in the person's own terms.</summary>
    public string Goal { get; }

    /// <summary>Gets the steps, in the order they will be attempted.</summary>
    public IReadOnlyList<AgentStep> Steps => _steps;

    /// <summary>Gets when the plan was made.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Gets the request the plan answers, exactly as it was made.</summary>
    public string OriginalRequest { get; }

    /// <summary>Gets where the request entered the agent from.</summary>
    public AgentRequestSource Source { get; }

    /// <summary>
    /// Gets why the plan looks like this, when there is something worth saying beyond the steps.
    /// Shown above the plan so a person can disagree with the approach before it starts, rather
    /// than after three steps have already searched their documents.
    /// </summary>
    public string? Explanation { get; }

    /// <summary>Gets how far through its life the plan is.</summary>
    public AgentPlanStatus Status { get; init; } = AgentPlanStatus.Draft;

    /// <summary>Gets the steps that call a tool this machine does not have.</summary>
    /// <remarks>
    /// Computed against the plan's own step list rather than stored, so it cannot disagree with
    /// the steps. Populated by the registry during validation and cleared when the plan is
    /// marked ready.
    /// </remarks>
    public IReadOnlyList<string> UnavailableTools { get; init; } = [];

    /// <summary>Gets a value indicating whether the plan has been checked and may run.</summary>
    public bool IsReady => Status == AgentPlanStatus.Ready || Status == AgentPlanStatus.Executing;

    /// <summary>
    /// Gets a value indicating whether a step of this plan is held at an approval gate right now.
    /// <para>
    /// Read from the steps rather than predicted, because a plan's risk is not known until the
    /// tool that would carry it out has said what it is about to do. A step that reads "write a
    /// report" is <see cref="ActionRiskLevel.Medium"/> in the hands of a report tool and
    /// harmless in the hands of a search tool, so the interface asks the plan rather than the
    /// planner.
    /// </para>
    /// </summary>
    public bool RequiresApproval => _steps.Any(step => step.Approval is not null);

    /// <summary>Gets the highest risk any step in this plan has actually declared.</summary>
    public ActionRiskLevel HighestRisk => _steps
        .Select(step => step.Approval?.RiskLevel ?? ActionRiskLevel.Low)
        .DefaultIfEmpty(ActionRiskLevel.Low)
        .Max();

    /// <summary>Gets the tools this plan will call, in order and without repeats.</summary>
    public IReadOnlyList<string> PlannedTools => _steps
        .Select(step => step.ToolName)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>Returns this plan marked as checked and ready to run.</summary>
    public AgentPlan AsReady() => this with { Status = AgentPlanStatus.Ready, UnavailableTools = [] };

    /// <summary>Returns this plan marked as running.</summary>
    public AgentPlan AsExecuting() => this with { Status = AgentPlanStatus.Executing };

    /// <summary>Returns this plan in a terminal state.</summary>
    public AgentPlan WithStatus(AgentPlanStatus status) => this with { Status = status };

    /// <summary>
    /// Returns this plan with a step replaced, which is how the executor records progress
    /// without being able to rewrite the step list itself.
    /// </summary>
    public AgentPlan WithStep(AgentStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        var updated = _steps.ToArray();
        var index = step.Order - 1;

        if (index < 0 || index >= updated.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(step),
                step.Order,
                "The step is not part of this plan.");
        }

        updated[index] = step;
        return new AgentPlan(Id, Goal, updated, CreatedAt, OriginalRequest, Source, Explanation)
        {
            Status = Status,
            UnavailableTools = UnavailableTools,
        };
    }

    /// <summary>Gets one step by its position.</summary>
    public AgentStep GetStep(int order)
    {
        var index = order - 1;

        if (index < 0 || index >= _steps.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(order),
                order,
                "The plan has no step at that position.");
        }

        return _steps[index];
    }

    /// <summary>Creates a one-step plan, which is what a direct request usually produces.</summary>
    public static AgentPlan Single(
        string goal,
        string toolName,
        string description,
        string originalRequest,
        AgentRequestSource source = AgentRequestSource.Text,
        string? reason = null,
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(goal);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        return new AgentPlan(
            Guid.NewGuid(),
            goal,
            [new AgentStep(1, toolName, description, reason, parameters)],
            DateTimeOffset.UtcNow,
            originalRequest,
            source);
    }

    /// <summary>Creates a plan from an ordered set of steps.</summary>
    public static AgentPlan Create(
        string goal,
        IEnumerable<AgentStep> steps,
        string originalRequest,
        AgentRequestSource source = AgentRequestSource.Text,
        string? explanation = null) =>
        new(Guid.NewGuid(), goal, steps, DateTimeOffset.UtcNow, originalRequest, source, explanation);

    /// <summary>
    /// Gets the plan as text a model can read: the goal, the original request, and each step's
    /// tool and purpose. Used to build the final answer from what the steps found, so the model
    /// summarises work that actually happened rather than the request that started it.
    /// </summary>
    public string BuildExecutionText()
    {
        var builder = new System.Text.StringBuilder();

        builder.AppendLine("Goal:");
        builder.AppendLine(Goal);
        builder.AppendLine();
        builder.AppendLine("Request:");
        builder.AppendLine(OriginalRequest);
        builder.AppendLine();
        builder.AppendLine("Steps that ran:");

        foreach (var step in _steps)
        {
            builder.Append(step.Order.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.Append(". ");
            builder.AppendLine(step.Description);
        }

        return builder.ToString();
    }
}
