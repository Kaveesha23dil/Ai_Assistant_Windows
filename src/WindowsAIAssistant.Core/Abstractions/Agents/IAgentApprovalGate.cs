using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Core.Abstractions.Agents;

/// <summary>
/// Holds the approvals a run is waiting on.
/// <para>
/// Separate from <see cref="IAgentExecutor"/> because the two ends of an approval are always in
/// different places. One end raises a question and waits, deep inside a run; the other end is a
/// button in a window, or a spoken answer, and has no idea a run exists. An interface that can
/// only reach the executor cannot answer, and an interface that only holds this can show a
/// prompt without being able to start a run — which is exactly what the workspace needs.
/// </para>
/// </summary>
public interface IAgentApprovalGate
{
    /// <summary>Gets how many approvals are outstanding right now.</summary>
    int PendingCount { get; }

    /// <summary>Raises an approval and waits for it to be answered, expired, or cancelled.</summary>
    Task<AgentApprovalDecision> RequestApprovalAsync(
        AgentApprovalRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers an outstanding approval.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when there was nothing to answer — already answered, already
    /// expired, or the run has ended. A late answer is not applied to anything.
    /// </returns>
    bool TryResolve(AgentApprovalDecision decision);
}
