using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Application.Agents;

/// <summary>
/// Keeps the run timeline in memory, for this session only, and bounded.
/// <para>
/// Two decisions are worth stating. The store is in memory by default, so a machine records
/// nothing about what somebody asked until a host supplies a durable one; and the number of
/// entries is capped, because a timeline that grows without limit on a long-running session is
/// a slow leak of exactly the information the rest of the agent is careful not to keep.
/// </para>
/// <para>
/// What each entry holds is decided by <see cref="AgentActivity"/> and not here: a goal, tool
/// names, counts, timings, and a status. There is no field on this store for document text,
/// because there is no field on the record for it either.
/// </para>
/// </summary>
public sealed class InMemoryAgentActivityStore : IAgentActivityStore
{
    /// <summary>
    /// How many runs are kept. Two hundred is roughly a working day of ordinary use and is
    /// bounded, so a machine left running over a long weekend does not accumulate a week of
    /// history nobody will read.
    /// </summary>
    private const int Capacity = 200;

    private readonly ConcurrentQueue<AgentActivity> _entries = new();
    private readonly object _trim = new();
    private readonly ILogger<InMemoryAgentActivityStore> _logger;

    public InMemoryAgentActivityStore(ILogger<InMemoryAgentActivityStore> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task RecordAsync(AgentActivity activity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activity);
        cancellationToken.ThrowIfCancellationRequested();

        _entries.Enqueue(activity);
        Trim();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AgentActivity>> GetRecentAsync(
        int count = 25,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Newest first, which is the order a timeline is read in. The count is clamped rather
        // than trusted, so a caller asking for ten thousand does not force a ten-thousand copy.
        IReadOnlyList<AgentActivity> recent = _entries
            .Reverse()
            .Take(Math.Clamp(count, 1, Capacity))
            .ToArray();

        return Task.FromResult(recent);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AgentActivity>> GetForConversationAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Oldest first, which is the order a conversation is read in. A run that belonged to no
        // conversation is not returned: the caller asked about one conversation, and a run from
        // the workspace is not part of it however recent it was.
        IReadOnlyList<AgentActivity> matching = _entries
            .Where(entry => entry.ConversationId == conversationId)
            .ToArray();

        return Task.FromResult(matching);
    }

    /// <inheritdoc />
    public Task<int> PruneAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var kept = _entries.Where(entry => entry.StartedAt >= olderThan).ToArray();
        var removed = _entries.Count - kept.Length;

        lock (_trim)
        {
            _entries.Clear();

            foreach (var entry in kept)
            {
                _entries.Enqueue(entry);
            }
        }

        return Task.FromResult(removed);
    }

    /// <inheritdoc />
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_trim)
        {
            _entries.Clear();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Drops the oldest entries past the cap. Guarded by a lock because two runs finishing at
    /// once would otherwise each rebuild the queue and one would lose the other's entry.
    /// </summary>
    private void Trim()
    {
        if (_entries.Count <= Capacity)
        {
            return;
        }

        lock (_trim)
        {
            while (_entries.Count > Capacity && _entries.TryDequeue(out _))
            {
                // Discarded deliberately. The count is what the cap is for.
            }
        }

        _logger.LogDebug("Trimmed the agent activity timeline to its {Capacity}-entry cap.", Capacity);
    }
}
