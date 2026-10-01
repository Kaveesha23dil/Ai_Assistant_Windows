using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Application.Agents;

/// <summary>
/// Keeps memories in memory, for this session only.
/// <para>
/// This is the deliberate default. A preference somebody stated in this conversation is useful
/// immediately and is not obviously something they asked to be kept afterwards, so nothing is
/// written to disk until a host supplies a store that does. The consequence is that the agent
    /// starts each morning not knowing what it learned yesterday, which is a feature rather than
/// /// a gap: it means the only things it remembers are the ones somebody is present to have
/// /// asked for.
/// </para>
/// <para>
/// A durable store replaces this by registering a different <see cref="IAgentMemoryStore"/>, and
/// nothing above this type changes — including the refusal rules, which are enforced above and
/// not here.
/// </para>
/// </summary>
public sealed class InMemoryAgentMemoryStore : IAgentMemoryStore
{
    private readonly ConcurrentDictionary<string, UserMemory> _memories =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ILogger<InMemoryAgentMemoryStore> _logger;

    public InMemoryAgentMemoryStore(ILogger<InMemoryAgentMemoryStore> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<UserMemory> SaveAsync(UserMemory memory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(memory);
        cancellationToken.ThrowIfCancellationRequested();

        _memories[Key(memory.MemoryType, memory.Key)] = memory;

        return Task.FromResult(memory);
    }

    /// <inheritdoc />
    public Task<UserMemory?> GetAsync(
        AgentMemoryType type,
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            _memories.TryGetValue(Key(type, key), out var memory) ? memory : null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<UserMemory>> ListAsync(
        AgentMemoryType type,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<UserMemory> ordered = _memories.Values
            .Where(memory => memory.MemoryType == type)
            .OrderByDescending(memory => memory.UpdatedAt)
            .ToArray();

        return Task.FromResult(ordered);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(
        AgentMemoryType type,
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_memories.TryRemove(Key(type, key), out _));
    }

    /// <inheritdoc />
    public Task<int> ClearAsync(AgentMemoryType? type = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (type is null)
        {
            var all = _memories.Count;
            _memories.Clear();
            return Task.FromResult(all);
        }

        var removed = 0;

        foreach (var pair in _memories.Where(pair => pair.Value.MemoryType == type.Value).ToArray())
        {
            if (_memories.TryRemove(pair.Key, out _))
            {
                removed++;
            }
        }

        return Task.FromResult(removed);
    }

    /// <summary>
    /// Builds the composite key, so a preference and a workflow of the same name are two
    /// different things rather than one overwriting the other.
    /// </summary>
    private static string Key(AgentMemoryType type, string key) => $"{type}:{key.Trim()}";
}
