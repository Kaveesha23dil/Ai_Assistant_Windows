using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Core.Abstractions.Agents;

/// <summary>
/// The assistant's front door: takes a request, plans it, runs it, and returns one answer.
/// <para>
/// This is the whole of the agent as a caller sees it. The chat page, the workspace, and the
/// voice path all come through here, which is what makes a typed request and a spoken one
/// behave the same: there is one sequence behind both, and it is not the caller's job to know
/// which parts of it they are allowed to skip.
/// </para>
/// <para>
/// The interface reports progress rather than returning a plan and leaving the caller to run it,
/// because every caller would otherwise have to reimplement the same four steps around the same
/// two services — and the fourth of those, waiting for a person, is the one most likely to be
/// got wrong.
/// </para>
/// </summary>
public interface IAgent
{
    /// <summary>
    /// Handles a request end to end and returns what the person should see or hear.
    /// </summary>
    /// <param name="context">The request, where it came from, and any conversation it belongs to.</param>
    /// <param name="cancellationToken">Cancels the run. A cancelled run is reported as cancelled, not as a failure.</param>
    Task<Result<AgentExecutionResult>> RunAsync(
        AgentRequestContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised as the run moves through its steps, so an interface can show what is happening
    /// without polling. The state carries the plan, the step, and the tool name — never the text
    /// a step read, which is the difference between a progress line and a transcript of somebody's
    /// documents.
    /// </summary>
    event EventHandler<AgentProgress>? ProgressChanged;

    /// <summary>Gets the plan of the run in flight, or <see langword="null"/> when nothing is running.</summary>
    AgentPlan? CurrentPlan { get; }

    /// <summary>Gets a value indicating whether a run is in progress right now.</summary>
    bool IsRunning { get; }

    /// <summary>
    /// Answers an approval the run is waiting on. The agent does not know what a person pressed;
    /// it is told, and the run continues or stops accordingly.
    /// </summary>
    Task RespondToApprovalAsync(AgentApprovalDecision decision);
}
