using System.Globalization;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Application.Agents;

/// <summary>
/// Remembers preferences, and refuses everything else.
/// <para>
/// The rules are checked here rather than in the store, so the store stays a place data goes
/// and this stays the place a decision is made. That way a caller cannot get a refusal by
/// forgetting to check — the only way in is <see cref="TryRememberAsync"/>, and it is the
/// method that decides.
/// </para>
/// <para>
/// Every refusal is logged with the reason and without the value. A log line saying "refused a
/// memory that looked like a credential" is genuinely useful; the same line with the credential
/// in it would be the incident.
/// </para>
/// </summary>
public sealed class AgentMemoryService : IAgentMemoryService
{
    private readonly IAgentMemoryStore _store;
    private readonly ILogger<AgentMemoryService> _logger;

    public AgentMemoryService(IAgentMemoryStore store, ILogger<AgentMemoryService> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<UserMemory>> TryRememberAsync(
        AgentMemoryType type,
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        if (!AgentMemoryRules.IsValidKey(key))
        {
            return Refuse("That is not a usable name for something to remember.");
        }

        if (!AgentMemoryRules.IsAllowed(value, out var refusal))
        {
            _logger.LogInformation(
                "Refused to remember '{Key}': {Reason}. The value was not logged.",
                key,
                refusal);

            return Refuse(refusal);
        }

        var memory = UserMemory.Create(type, key.Trim(), value.Trim());

        try
        {
            return Result<UserMemory>.Success(await _store.SaveAsync(memory, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not write a memory for '{Key}'.", key);

            return Result<UserMemory>.Failure(
                ErrorCodes.AgentMemoryUnavailable,
                "I could not remember that just now.");
        }
    }

    /// <inheritdoc />
    public async Task<UserMemory?> RecallAsync(
        AgentMemoryType type,
        string key,
        CancellationToken cancellationToken = default)
    {
        if (!AgentMemoryRules.IsValidKey(key))
        {
            return null;
        }

        try
        {
            return await _store.GetAsync(type, key, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not read a memory for '{Key}'.", key);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<UserMemory?> RecallAndCountAsync(
        AgentMemoryType type,
        string key,
        CancellationToken cancellationToken = default)
    {
        var memory = await RecallAsync(type, key, cancellationToken);

        if (memory is null)
        {
            return null;
        }

        try
        {
            return await _store.SaveAsync(memory.AsUsed(), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Failing to count a use is not a reason to withhold the memory. Somebody asked what
            // was remembered and the answer is known; the tally is bookkeeping.
            _logger.LogDebug(exception, "Could not count a use of '{Key}'.", key);
            return memory;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserMemory>> ListAsync(
        AgentMemoryType type,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _store.ListAsync(type, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not list the stored memories.");
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<bool> ForgetAsync(
        AgentMemoryType type,
        string key,
        CancellationToken cancellationToken = default)
    {
        if (!AgentMemoryRules.IsValidKey(key))
        {
            return false;
        }

        try
        {
            return await _store.DeleteAsync(type, key, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not forget '{Key}'.", key);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<int> ForgetAllAsync(
        AgentMemoryType? type = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var removed = await _store.ClearAsync(type, cancellationToken);

            _logger.LogInformation("Forgot {Count} stored memory/memories.", removed);

            return removed;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not clear the stored memories.");
            return 0;
        }
    }

    private static Result<UserMemory> Refuse(string reason) =>
        Result<UserMemory>.Failure(ErrorCodes.AgentMemoryRefused, reason);
}
