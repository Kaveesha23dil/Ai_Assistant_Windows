using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Core.Abstractions.Agents;

/// <summary>
/// Remembers preferences, and refuses to remember the things that must not be kept.
/// <para>
/// The refusal is the important half of this interface. A memory store that only ever accepts
/// would be safe by omission; one that can be asked to store a password and quietly does so is
/// the failure this is designed to make impossible, so <see cref="TryRemember"/> reports a
/// refusal as a result rather than as an exception, and the rules live in
/// <see cref="AgentMemoryRules"/> where they can be read and tested on their own.
/// </para>
/// <para>
/// Nothing read from a document or a screen belongs here. A memory is something a person told
/// the assistant about itself — a preferred report format, a usual working folder — and the
/// rules enforce a length and a vocabulary that a document passage would not fit into.
/// </para>
/// </summary>
public interface IAgentMemoryService
{
    /// <summary>
    /// Remembers something, or refuses to.
    /// <para>
    /// Remembering the same key twice replaces the earlier value rather than accumulating a
    /// second one, because a person who changes their mind is updating a preference, not making
    /// a list.
    /// </para>
    /// </summary>
    /// <returns>
    /// The stored memory, or a failure carrying <see cref="ErrorCodes.AgentMemoryRefused"/> and
    /// a sentence saying what was wrong with it.
    /// </returns>
    Task<Result<UserMemory>> TryRememberAsync(
        AgentMemoryType type,
        string key,
        string value,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a memory by key, ignoring case. Null when there is none.</summary>
    Task<UserMemory?> RecallAsync(AgentMemoryType type, string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a memory and counts that it was used, so a preference nobody has relied on in months
    /// can be noticed and offered for removal.
    /// </summary>
    Task<UserMemory?> RecallAndCountAsync(
        AgentMemoryType type,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the memories of one kind, most recently changed first.</summary>
    Task<IReadOnlyList<UserMemory>> ListAsync(
        AgentMemoryType type,
        CancellationToken cancellationToken = default);

    /// <summary>Forgets one memory. Returns false when there was nothing to forget.</summary>
    Task<bool> ForgetAsync(AgentMemoryType type, string key, CancellationToken cancellationToken = default);

    /// <summary>Forgets every memory of one kind, for a "clear what you know" control.</summary>
    Task<int> ForgetAllAsync(AgentMemoryType? type = null, CancellationToken cancellationToken = default);
}
