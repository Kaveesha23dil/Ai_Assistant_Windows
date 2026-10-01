using System.Globalization;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Agents;

/// <summary>
/// One entry in the activity timeline: what the agent was asked to do, and how it went.
/// <para>
/// This is the whole of what the agent is allowed to remember about a run. It carries the goal,
/// the tool names, a count, a duration, and a status. It does not carry the request text, the
/// answer, a passage from a document, a line of screen text, or the prompt that was sent to a
/// provider — and none of those is available to it, so no tool can leak one by accident.
/// </para>
/// <para>
/// The goal is itself a summary rather than the original words. "Create the weekly project
/// report" is a fine thing to keep; the paragraph somebody typed that led to it is their
/// business, and a timeline that stored it would be a second copy of their documents.
/// </para>
/// </summary>
public sealed record AgentActivity
{
    public AgentActivity(
        Guid id,
        Guid runId,
        string goal,
        DateTimeOffset startedAt,
        AgentRequestSource source,
        AgentActivityStatus status = AgentActivityStatus.Running,
        Guid? conversationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(goal);

        Id = id;
        RunId = runId;
        Goal = goal.Trim();
        StartedAt = startedAt;
        Source = source;
        Status = status;
        ConversationId = conversationId;
    }

    /// <summary>Gets the identifier of this entry.</summary>
    public Guid Id { get; }

    /// <summary>Gets the run this entry describes.</summary>
    public Guid RunId { get; }

    /// <summary>
    /// Gets the conversation this run belonged to, or <see langword="null"/> when it did not
    /// belong to one.
    /// <para>
    /// An identifier and nothing else. It is what lets a chat view list the runs belonging to
    /// one conversation without keeping the conversation, and it is deliberately not a reference
    /// to anything that would pull the messages back with it.
    /// </para>
    /// </summary>
    public Guid? ConversationId { get; }

    /// <summary>Gets what the agent was asked to do, as a summary rather than the original words.</summary>
    public string Goal { get; }

    /// <summary>Gets when the run started.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>Gets where the request came from.</summary>
    public AgentRequestSource Source { get; }

    /// <summary>Gets how the run ended.</summary>
    public AgentActivityStatus Status { get; init; }

    /// <summary>Gets when the run finished, or <see langword="null"/> while it is still running.</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>
    /// Gets the tools that ran, in order. Names only: a tool name is a description of what the
    /// assistant can do, and is the same on every machine that has it.
    /// </summary>
    public IReadOnlyList<string> ToolsUsed { get; init; } = [];

    /// <summary>Gets how long the run took.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Gets how many sources the run produced. A count is safe to keep in a way the sources are
    /// not: it says the answer was grounded without preserving any of it.
    /// </summary>
    public int SourceCount { get; init; }

    /// <summary>Gets the stable code when the run failed, otherwise <see langword="null"/>.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Gets the one-line note shown under the goal in the timeline. It is written by the agent
    /// from counts and tool names, never quoted from a step's content.
    /// </summary>
    public string? Note { get; init; }

    /// <summary>Gets a value indicating whether the run is still going.</summary>
    public bool IsRunning => Status == AgentActivityStatus.Running;

    /// <summary>Returns this entry marked as finished.</summary>
    public AgentActivity AsFinished(
        AgentActivityStatus status,
        IReadOnlyList<string> toolsUsed,
        TimeSpan duration,
        int sourceCount,
        string? note = null,
        string? errorCode = null) => this with
        {
            Status = status,
            CompletedAt = DateTimeOffset.UtcNow,
            ToolsUsed = toolsUsed,
            Duration = duration,
            SourceCount = sourceCount,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            ErrorCode = errorCode,
        };

    /// <summary>
    /// Builds the one-line note for a finished run from its metadata alone, so the timeline
    /// entry is a function of the plan and the counts rather than of anything a step read.
    /// </summary>
    public static string BuildNote(
        IReadOnlyList<string> toolsUsed,
        int sourceCount,
        int stepCount)
    {
        var tools = toolsUsed.Count == 0
            ? "no tools"
            : string.Join(", ", toolsUsed);

        var sources = sourceCount == 0
            ? "no sources"
            : $"{sourceCount.ToString(CultureInfo.InvariantCulture)} source(s)";

        return $"{tools} — {sources} — {stepCount.ToString(CultureInfo.InvariantCulture)} step(s)";
    }

    /// <summary>
    /// Builds the entry for a run that has just finished.
    /// <para>
    /// The goal is taken from the plan and everything else from counts, so what is written to the
    /// database is a description of the run rather than any part of what it read. The note is
    /// built here from that same metadata, which means the timeline cannot show a passage from a
    /// document even if a caller passes one in.
    /// </para>
    /// </summary>
    public static AgentActivity FromResult(AgentRequestContext context, AgentExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(result);

        var status = result.Status switch
        {
            AgentPlanStatus.Completed => AgentActivityStatus.Completed,
            AgentPlanStatus.Cancelled => AgentActivityStatus.Cancelled,
            AgentPlanStatus.Rejected => AgentActivityStatus.Rejected,
            _ => AgentActivityStatus.Failed,
        };

        return new AgentActivity(
            Guid.NewGuid(),
            result.RunId,
            result.Plan.Goal,
            DateTimeOffset.UtcNow,
            context.Source,
            conversationId: context.ConversationId)
        {
            Status = status,
            CompletedAt = DateTimeOffset.UtcNow,
            ToolsUsed = result.ToolsUsed,
            Duration = result.Duration,
            SourceCount = result.Sources.Count,
            ErrorCode = result.ErrorCode,
            Note = BuildNote(result.ToolsUsed, result.Sources.Count, result.Steps.Count),
        };
    }

    /// <summary>
    /// Builds the entry for a run that never got as far as running — a request that could not be
    /// understood, or a plan that could not be checked.
    /// <para>
    /// Recorded rather than swallowed, because a machine where every request is refused looks
    /// exactly like a machine that is not being asked anything, and the timeline is the only place
    /// that distinguishes them.
    /// </para>
    /// <para>
    /// The goal is a fixed description and not the request. It is tempting to store a truncated
    /// copy of what was asked, on the reasoning that 120 characters is not much — but the request
    /// is exactly the text that may be somebody's document contents, a dictated passage, or a
    /// password they were checking the calculator on, and a truncated copy is a copy. The refusal
    /// is still fully legible from the note, which is the part written by this code rather than
    /// taken from the person.
    /// </para>
    /// </summary>
    public static AgentActivity Failed(AgentRequestContext context, string message, string? errorCode = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new AgentActivity(
            Guid.NewGuid(),
            Guid.NewGuid(),
            GoalForUnplannedRequest(context.Source),
            DateTimeOffset.UtcNow,
            context.Source,
            conversationId: context.ConversationId)
        {
            Status = AgentActivityStatus.Failed,
            CompletedAt = DateTimeOffset.UtcNow,
            ErrorCode = errorCode,
            Note = message,
        };
    }

    /// <summary>
    /// The goal recorded for a request that never became a plan. Says where it came from and that
    /// it did not get as far as being understood, and carries nothing the person typed.
    /// </summary>
    private static string GoalForUnplannedRequest(AgentRequestSource source) => source switch
    {
        AgentRequestSource.Voice => "A spoken request that was not understood",
        AgentRequestSource.DemoScenario => "A demonstration that could not be started",
        AgentRequestSource.QuickAction => "A quick action that could not be started",
        _ => "A request that was not understood",
    };

    /// <summary>Gets a one-line form for a log entry.</summary>
    public override string ToString() =>
        $"{StartedAt:HH:mm} {Goal} ({Status})";
}
