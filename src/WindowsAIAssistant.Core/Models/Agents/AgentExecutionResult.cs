using System.Globalization;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Agents;

/// <summary>
/// What came of running a plan: every step's outcome, the sentence for the person, and where
/// it stopped if it did not finish.
/// <para>
/// The per-step outcomes are the result, not an afterthought. "The report could not be saved"
/// is only actionable if the person can see that the search before it found fifteen documents
/// and the write after it was the part that failed.
/// </para>
/// <para>
/// A run that was cancelled is not a failed run. <see cref="WasCancelled"/> keeps the two apart,
/// because a person who pressed stop has not encountered a problem and should not be shown an
/// error message telling them to try again.
/// </para>
/// </summary>
public sealed record AgentExecutionResult
{
    public AgentExecutionResult(
        Guid runId,
        AgentPlan plan,
        IReadOnlyList<AgentStep> steps,
        string finalResponse,
        AgentPlanStatus status,
        TimeSpan duration,
        IReadOnlyList<string> sources,
        IReadOnlyDictionary<string, string>? data)
    {
        RunId = runId;
        Plan = plan;
        Steps = steps;
        FinalResponse = finalResponse;
        Status = status;
        Duration = duration;
        Sources = sources;
        Data = data ?? EmptyData;
    }

    /// <summary>
    /// Stands in for an absent metadata map. Empty rather than <see langword="null"/> because
    /// every reader of <see cref="Data"/> would otherwise have to handle a state that a caller
    /// passing nothing already describes.
    /// </summary>
    private static IReadOnlyDictionary<string, string> EmptyData { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the identifier of the run, shared with the plan and the activity record.</summary>
    public Guid RunId { get; }

    /// <summary>Gets the plan that was run, with every step released of its result.</summary>
    public AgentPlan Plan { get; }

    /// <summary>Gets the outcome of each step, in the order the plan listed them.</summary>
    public IReadOnlyList<AgentStep> Steps { get; }

    /// <summary>Gets the sentence to show or speak. Never empty, even for a run that failed.</summary>
    public string FinalResponse { get; }

    /// <summary>Gets how the run ended.</summary>
    public AgentPlanStatus Status { get; }

    /// <summary>Gets how long the whole run took.</summary>
    public TimeSpan Duration { get; }

    /// <summary>Gets the places the answer came from, collected from every step that produced some.</summary>
    public IReadOnlyList<string> Sources { get; }

    /// <summary>
    /// Gets the metadata about the run: which tools ran, how many sources, where a file was
    /// written. This is the whole of what may be written to the activity timeline.
    /// </summary>
    public IReadOnlyDictionary<string, string> Data { get; init; }

    /// <summary>Gets a value indicating whether every step finished successfully.</summary>
    public bool IsSuccess => Status == AgentPlanStatus.Completed;

    /// <summary>Gets a value indicating whether the person stopped the run.</summary>
    public bool WasCancelled => Status == AgentPlanStatus.Cancelled;

    /// <summary>Gets a value indicating whether a refused approval stopped the run.</summary>
    public bool WasRejected => Status == AgentPlanStatus.Rejected;

    /// <summary>Gets the tool names that ran, in order, for the timeline and the log.</summary>
    public IReadOnlyList<string> ToolsUsed => Steps
        .Where(step => step.Status is AgentStepStatus.Succeeded)
        .Select(step => step.ToolName)
        .ToArray();

    /// <summary>Gets the step the run stopped at, or <see langword="null"/> if it finished.</summary>
    public AgentStep? StoppedAt => Steps.FirstOrDefault(
        step => step.Status is AgentStepStatus.Failed or AgentStepStatus.Rejected);

    /// <summary>Gets the stable code when the run did not finish, otherwise <see langword="null"/>.</summary>
    public string? ErrorCode => StoppedAt?.ErrorCode;

    /// <summary>Creates the result of a run that finished.</summary>
    public static AgentExecutionResult Completed(
        Guid runId,
        AgentPlan plan,
        IReadOnlyList<AgentStep> steps,
        string finalResponse,
        TimeSpan duration,
        IReadOnlyList<string> sources,
        IReadOnlyDictionary<string, string>? data = null) =>
        new(runId, plan, steps, finalResponse, AgentPlanStatus.Completed, duration, sources, data);

    /// <summary>Creates the result of a run that stopped early.</summary>
    public static AgentExecutionResult Stopped(
        Guid runId,
        AgentPlan plan,
        IReadOnlyList<AgentStep> steps,
        string finalResponse,
        AgentPlanStatus status,
        TimeSpan duration,
        IReadOnlyList<string> sources,
        IReadOnlyDictionary<string, string>? data = null,
        string? errorCode = null) =>
        new(runId, plan, steps, finalResponse, status, duration, sources, data)
        {
            Data = errorCode is null
                ? (data ?? EmptyData)
                : new Dictionary<string, string>(data ?? EmptyData, StringComparer.OrdinalIgnoreCase)
                {
                    ["errorCode"] = errorCode,
                },
        };

    /// <summary>
    /// Gets the run as text a model can read to write the final answer, containing the goal and
    /// what each step found.
    /// <para>
    /// This is the boundary where document text, screen text, and search results stop being
    /// passed to a provider at all. Everything above it is metadata; this is the last point at
    /// which a step's content is read, and it exists in exactly one place so that reading it can
    /// be reviewed as a single decision rather than as a habit spread across seven tools.
    /// </para>
    /// </summary>
    public string BuildEvidenceText() => Plan.BuildExecutionText();
}
