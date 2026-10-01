using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Infrastructure.Agents;

/// <summary>
/// Keeps the agent's memories in its local SQLite file.
/// <para>
/// A store and not a service, and stateless: it opens a connection per call and closes it again,
/// so the workspace reading a preference, a run recording a workflow, and a demonstration writing
/// a memory at the same moment cannot block one another behind a shared handle.
/// </para>
/// <para>
/// What may be written is decided by <see cref="AgentMemoryRules"/> in the Application layer,
/// before anything reaches here. That separation is deliberate. This type accepts any string it
/// is handed, which sounds like a hole and is not: a tool that tried to store a password would
/// have to get past the service first, and the service does not trust the tool or the planner
/// that asked it. Putting the rules here instead would mean trusting every future caller to
/// remember them.
/// </para>
/// <para>
/// The alternative to this type — the session-only store — has the same contract and the same
/// absence of rules. Swapping one for the other changes where the rows go and nothing about what
/// they may contain.
/// </para>
/// </summary>
public sealed class SqliteAgentMemoryStore : IAgentMemoryStore
{
    private const string Columns = "Id, MemoryType, Key, Value, CreatedAt, UpdatedAt, UseCount";

    private readonly AgentDatabase _database;
    private readonly ILogger<SqliteAgentMemoryStore> _logger;

    public SqliteAgentMemoryStore(AgentDatabase database, ILogger<SqliteAgentMemoryStore> logger)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(logger);

        _database = database;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<UserMemory> SaveAsync(UserMemory memory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(memory);

        try
        {
            await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();

            // An upsert rather than a delete-then-insert, so the replace happens inside one
            // statement and a reader cannot see the moment at which the row does not exist.
            // UseCount is carried over deliberately: remembering a preference again is an update
            // to it, and resetting how often it has been used would make a stale preference look
            // fresh every time somebody restates it.
            command.CommandText = $"""
                INSERT INTO Memories ({Columns})
                VALUES ($id, $type, $key, $value, $createdAt, $updatedAt, $useCount)
                ON CONFLICT (MemoryType, Key COLLATE NOCASE) DO UPDATE SET
                    Id = excluded.Id,
                    Value = excluded.Value,
                    UpdatedAt = excluded.UpdatedAt,
                    UseCount = Memories.UseCount;
                """;

            command.Parameters.AddWithValue("$id", memory.Id.ToString());
            command.Parameters.AddWithValue("$type", memory.MemoryType.ToString());
            command.Parameters.AddWithValue("$key", memory.Key);
            command.Parameters.AddWithValue("$value", memory.Value);
            command.Parameters.AddWithValue("$createdAt", memory.CreatedAt.ToStorage());
            command.Parameters.AddWithValue("$updatedAt", memory.UpdatedAt.ToStorage());
            command.Parameters.AddWithValue("$useCount", memory.UseCount);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            return memory;
        }
        catch (SqliteException ex)
        {
            _logger.LogError(ex, "A memory could not be written to the agent store.");
            throw new AgentException(
                "I could not save that preference just now.",
                ErrorCodes.AgentMemoryUnavailable,
                ex);
        }
    }

    /// <inheritdoc />
    public async Task<UserMemory?> GetAsync(
        AgentMemoryType type,
        string key,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        // NOCASE on both sides, matching the index, so a lookup and a list agree about what
        // counts as the same key. A lookup that ignored case while the write did not would let
        // two rows exist for one preference.
        command.CommandText = $"""
            SELECT {Columns} FROM Memories
            WHERE MemoryType = $type AND Key = $key COLLATE NOCASE
            LIMIT 1;
            """;

        command.Parameters.AddWithValue("$type", type.ToString());
        command.Parameters.AddWithValue("$key", key.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserMemory>> ListAsync(
        AgentMemoryType type,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = $"""
            SELECT {Columns} FROM Memories
            WHERE MemoryType = $type
            ORDER BY UpdatedAt DESC, Key ASC;
            """;

        command.Parameters.AddWithValue("$type", type.ToString());

        var results = new List<UserMemory>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(Read(reader));
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        AgentMemoryType type,
        string key,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = "DELETE FROM Memories WHERE MemoryType = $type AND Key = $key COLLATE NOCASE;";
        command.Parameters.AddWithValue("$type", type.ToString());
        command.Parameters.AddWithValue("$key", key.Trim());

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    public async Task<int> ClearAsync(
        AgentMemoryType? type = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        if (type is null)
        {
            command.CommandText = "DELETE FROM Memories;";
        }
        else
        {
            command.CommandText = "DELETE FROM Memories WHERE MemoryType = $type;";
            command.Parameters.AddWithValue("$type", type.Value.ToString());
        }

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads a row into a memory.
    /// <para>
    /// A row that cannot be read is skipped rather than thrown on. A preferences table that has
    /// been edited by hand, or written by a newer build with a value this one does not
    /// understand, should cost somebody one missing preference and not a workspace that will not
    /// open.
    /// </para>
    /// </summary>
    private static UserMemory Read(SqliteDataReader reader)
    {
        var memory = new UserMemory(
            Guid.Parse(reader.GetString(0)),
            Enum.Parse<AgentMemoryType>(reader.GetString(1), ignoreCase: true),
            reader.GetString(2),
            reader.GetString(3),
            reader.ReadTimestamp(4),
            reader.ReadTimestamp(5))
        {
            UseCount = reader.GetInt32(6),
        };

        return memory;
    }

    /// <summary>Logs a summary of what the store holds, for a start-up line.</summary>
    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT COUNT(*) FROM Memories;";

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "The agent store holds {Count} memory row(s).",
            result is null or DBNull
                ? 0
                : Convert.ToInt32(result, CultureInfo.InvariantCulture));

        return result is null or DBNull ? 0 : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }
}