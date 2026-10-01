using System.Globalization;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Agents;

/// <summary>
/// One step of a plan: a tool, what it is for, and what happened when it ran.
/// <para>
/// The step is a record rather than a description. A plan and an execution are different things
/// and conflating them is how a half-finished run comes to be shown as though every step of it
/// had completed. <see cref="Status"/> starts at <see cref="AgentStepStatus.Pending"/> and is
/// only ever set by the executor.
/// </para>
/// <para>
/// <see cref="Result"/> is dropped once a run ends, deliberately. It is the only place a
/// document's text or a screenshot's transcription would live in a plan, and a plan is the
/// object most likely to be handed to an interface, logged, or kept. What survives is
/// <see cref="ResultSummary"/>: a line of prose saying what came of it, which is what the
/// timeline shows and the only thing about a step that outlives the run.
/// </para>
/// </summary>
public sealed record AgentStep
{
    public AgentStep(
        int order,
        string toolName,
        string description,
        string? reason = null,
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        if (order < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(order), order, "Steps are numbered from one.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Order = order;
        ToolName = toolName.Trim();
        Description = description.Trim();
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Parameters = parameters ?? EmptyParameters;
    }

    /// <summary>
    /// Creates a step without numbering it, for a planner that is building a list rather than a
    /// finished plan.
    /// <para>
    /// The number written here is a placeholder. <see cref="AgentPlan"/> renumbers every step it
    /// is given, so a planner can hand over steps in any order, with gaps, or duplicated, and the
    /// plan that comes out is numbered correctly. Numbering at this point as well would only give
    /// a planner a second place to get it wrong.
    /// </para>
    /// </summary>
    public static AgentStep Create(
        string toolName,
        string description,
        string? reason = null,
        IReadOnlyDictionary<string, string>? parameters = null) =>
        new(1, toolName, description, reason, parameters);

    /// <summary>Gets this step's position in the plan, counted from one.</summary>
    public int Order { get; init; }

    /// <summary>Gets the registered name of the tool this step calls.</summary>
    public string ToolName { get; init; }

    /// <summary>Gets what the step is for, phrased for a person reading the timeline.</summary>
    public string Description { get; init; }

    /// <summary>
    /// Gets why the planner chose this step. Kept because "search the knowledge base, then
    /// write a report" is only reassuring to somebody who can see that the search was not an
    /// accident.
    /// </summary>
    public string? Reason { get; }

    /// <summary>Gets the named values handed to the tool.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; }

    /// <summary>Gets what has happened to this step so far.</summary>
    public AgentStepStatus Status { get; init; } = AgentStepStatus.Pending;

    /// <summary>
    /// Gets the raw result while the run is in flight, and <see langword="null"/> once it has
    /// finished. See the type remarks for why it does not survive the run.
    /// </summary>
    public ToolResult? Result { get; init; }

    /// <summary>Gets a one-line account of what the step produced, safe to keep and to show.</summary>
    public string? ResultSummary { get; init; }

    /// <summary>Gets how long the step took, once it has run.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Gets the stable code when the step failed, otherwise <see langword="null"/>.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Gets the sentence to show when the step failed, otherwise <see langword="null"/>.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets the approval the executor raised for this step, or <see langword="null"/> if none was
    /// needed. Held so the interface can show which step is waiting rather than a bare "waiting".
    /// </summary>
    public AgentApprovalRequest? Approval { get; init; }

    /// <summary>Gets how the person answered the approval, when they were asked.</summary>
    public AgentApprovalDecision? ApprovalDecision { get; init; }

    /// <summary>Gets a value indicating whether this step has finished, one way or another.</summary>
    public bool IsFinished => Status is not (AgentStepStatus.Pending or AgentStepStatus.Running);

    /// <summary>Gets a value indicating whether the step produced usable output.</summary>
    public bool Succeeded => Status == AgentStepStatus.Succeeded;

    /// <summary>Returns this step marked as running.</summary>
    public AgentStep AsRunning() => this with { Status = AgentStepStatus.Running };

    /// <summary>
    /// Returns this step marked as finished with a result.
    /// <para>
    /// The result is stored only for the run. <see cref="ResultSummary"/> is written here, from
    /// the tool's own metadata, so that the timeline has something to show without ever being
    /// handed the text a step read.
    /// </para>
    /// </summary>
    public AgentStep AsSucceeded(ToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return this with
        {
            Status = AgentStepStatus.Succeeded,
            Result = result,
            ResultSummary = Summarize(result),
            Duration = result.Duration,
            ErrorCode = null,
            ErrorMessage = null,
        };
    }

    /// <summary>Returns this step marked as failed, with the code and the sentence to show.</summary>
    public AgentStep AsFailed(string errorCode, string errorMessage) => this with
    {
        Status = AgentStepStatus.Failed,
        Result = null,
        ResultSummary = errorMessage,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage,
    };

    /// <summary>Returns this step marked as deliberately not attempted.</summary>
    public AgentStep AsSkipped() => this with
    {
        Status = AgentStepStatus.Skipped,
        Result = null,
    };

    /// <summary>Returns this step held at the approval gate.</summary>
    public AgentStep AwaitingApproval(AgentApprovalRequest approval)
    {
        ArgumentNullException.ThrowIfNull(approval);
        return this with { Status = AgentStepStatus.AwaitingApproval, Approval = approval };
    }

    /// <summary>Returns this step marked as refused by the person.</summary>
    public AgentStep AsRejected(AgentApprovalDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        return this with
        {
            Status = AgentStepStatus.Rejected,
            ApprovalDecision = decision,
            Result = null,
            ResultSummary = decision.ResponseText,
        };
    }

    /// <summary>Returns this step with the result released and the timing kept.</summary>
    /// <remarks>
    /// Called once the run ends. After this the step holds a summary, a status, and a duration,
    /// and nothing else — so a plan that is kept, logged, or shown later cannot contain a
    /// passage from anybody's documents.
    /// </remarks>
    public AgentStep WithoutResult() => this with { Result = null };

    /// <summary>Gets the one-line form of this step, for a log line or a speech response.</summary>
    public override string ToString() =>
        $"{Order.ToString(CultureInfo.InvariantCulture)}. {Description} ({Status})";

    /// <summary>
    /// Builds the line the timeline shows from the tool's own metadata, never from its content.
    /// A tool reports one of "found" or "wrote" in its data; anything else falls back to the
    /// description, so an unfamiliar tool still produces a readable row.
    /// </summary>
    private static string Summarize(ToolResult result)
    {
        if (result.Data.TryGetValue("summary", out var summary) && !string.IsNullOrWhiteSpace(summary))
        {
            return summary;
        }

        if (result.Sources.Count > 0)
        {
            return $"Found {result.Sources.Count.ToString(CultureInfo.InvariantCulture)} source(s).";
        }

        return result.HasContent ? "Completed." : "Nothing found.";
    }

    private static IReadOnlyDictionary<string, string> EmptyParameters { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
