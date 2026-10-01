using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Core.Abstractions.Agents;

/// <summary>
/// Where memories actually live.
/// <para>
/// A storage interface with no rules in it, so the same store can be pointed at a database, a
/// file, or nothing at all in a test. Everything about what may be written is decided in
/// <see cref="IAgentMemoryService"/> before a value reaches here, which is what makes it safe
/// for this interface to accept any string it is handed.
/// </para>
/// </summary>
public interface IAgentMemoryStore
{
    /// <summary>
    /// Writes a memory, replacing any existing one with the same type and key.
    /// <para>
    /// Replace rather than insert, so remembering a preference a second time updates it and does
    /// not leave the old value behind for a reader to find.
    /// </para>
    /// </summary>
    Task<UserMemory> SaveAsync(UserMemory memory, CancellationToken cancellationToken = default);

    /// <summary>Reads one memory by type and key, ignoring case. Null when there is none.</summary>
    Task<UserMemory?> GetAsync(
        AgentMemoryType type,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the memories of one kind, most recently changed first.</summary>
    Task<IReadOnlyList<UserMemory>> ListAsync(
        AgentMemoryType type,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes one memory. Returns false when there was nothing to delete.</summary>
    Task<bool> DeleteAsync(
        AgentMemoryType type,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes every memory, or every memory of one kind. Returns how many went.</summary>
    Task<int> ClearAsync(AgentMemoryType? type = null, CancellationToken cancellationToken = default);
}
