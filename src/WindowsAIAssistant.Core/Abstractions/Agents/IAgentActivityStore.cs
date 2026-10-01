using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Core.Abstractions.Agents;

/// <summary>
/// The timeline of what the agent has done.
/// <para>
/// An append-and-read store of runs, holding one line per run: what was asked for, which tools
/// ran, how long it took, how many sources the answer had, and whether it finished. It holds no
/// document text, no screen text, and no prompt, and the type it stores is built to make that
/// hard rather than merely intended — the thing recorded is counts and names, so there is
/// nothing to leak even if a caller passes more than it should.
/// </para>
/// <para>
/// An interface in Core rather than in Application so the workspace can read the timeline
/// without knowing how it is kept, and so a test can stand in a store that remembers nothing.
/// </para>
/// </summary>
public interface IAgentActivityStore
{
    /// <summary>Records one run. Called once per run, whether it finished or not.</summary>
    Task RecordAsync(AgentActivity activity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the most recent runs, newest first. The count is honoured but not trusted: a caller
    /// asking for a thousand gets what the store keeps, which is bounded.
    /// </summary>
    Task<IReadOnlyList<AgentActivity>> GetRecentAsync(
        int count = 25,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the runs for one conversation, oldest first, for a chat view.</summary>
    Task<IReadOnlyList<AgentActivity>> GetForConversationAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes entries older than a cut-off. Returns how many were removed.</summary>
    Task<int> PruneAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);

    /// <summary>Deletes every entry. Forgets nothing else — this store holds no other data.</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}
