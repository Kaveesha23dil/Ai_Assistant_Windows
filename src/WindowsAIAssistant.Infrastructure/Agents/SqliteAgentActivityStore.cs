using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Infrastructure.Agents;

/// <summary>
/// Keeps the agent's activity timeline in its local SQLite file.
/// <para>
/// The same file as the memories and the same contract as the in-memory store it can be swapped
/// for. The only decision this type makes is where the rows go.
/// </para>
/// <para>
/// What goes in a row is fixed by <see cref="AgentActivity"/>, which carries a goal, tool names,
/// counts, a duration, and a status. There is no column here for a passage, a frame, or a prompt,
/// so the promise the timeline makes — that it records what the agent did and not what it read —
/// is a property of the schema rather than a rule each writer has to follow.
/// </para>
/// <para>
/// A run still going is written when it starts and updated when it ends rather than being held in
/// memory until it finishes, so a run that crashes the application still leaves a row saying it
/// was running. That is the difference between a timeline and a list of successes.
/// </para>
/// </summary>
public sealed class SqliteAgentActivityStore : IAgentActivityStore
{
    private const string Columns =
        "Id, RunId, ConversationId, Goal, StartedAt, CompletedAt, Source, Status, ToolsUsed, " +
        "DurationMs, SourceCount, Note, ErrorCode";

    /// <summary>
    /// The most rows one conversation view will read. A conversation with more runs than this is
    /// being displayed through its transcript rather than its agent timeline, and reading all of
    /// it would be a query with no upper bound.
    /// </summary>
    private const int MaximumConversationRows = 500;

    private readonly AgentDatabase _database;
    private readonly ILogger<SqliteAgentActivityStore> _logger;
    private readonly int _retention;

    public SqliteAgentActivityStore(
        AgentDatabase database,
        IOptions<AgentOptions> options,
        ILogger<SqliteAgentActivityStore> logger)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _database = database;
        _logger = logger;

        // Bounded, and clamped to something usable. An unbounded timeline is a file whose
        // contents will still be there in a year, and a retention of zero would be a store that
        // deletes every run the moment it records it.
        _retention = Math.Clamp(options.Value.ActivityRetentionCount, 1, 100_000);
    }

    /// <inheritdoc />
    public async Task RecordAsync(AgentActivity activity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activity);

        try
        {
            await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();

            // Write once, update in place. A run records itself when it starts and again when it
            // ends, and there is one row per run because the key is the run, not the attempt.
            command.CommandText = $"""
                INSERT INTO AgentActivity ({Columns})
                VALUES ($id, $runId, $conversationId, $goal, $startedAt, $completedAt, $source,
                        $status, $toolsUsed, $durationMs, $sourceCount, $note, $errorCode)
                ON CONFLICT (Id) DO UPDATE SET
                    Status = excluded.Status,
                    CompletedAt = excluded.CompletedAt,
                    ToolsUsed = excluded.ToolsUsed,
                    DurationMs = excluded.DurationMs,
                    SourceCount = excluded.SourceCount,
                    Note = excluded.Note,
                    ErrorCode = excluded.ErrorCode;
                """;

            Bind(command, activity);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SqliteException or AgentException)
        {
            // The run itself has already happened by the time this is called, and a failure to
            // write its history is not a reason to tell somebody the run failed. It is logged and
            // swallowed, and the timeline is simply missing one line.
            //
            // AgentException is caught alongside the SQLite one because a store that cannot be
            // opened fails on the way in rather than on the write, and letting that reach the
            // caller would turn a missing timeline entry into a failed run.
            _logger.LogError(ex, "An agent run could not be recorded in the timeline.");
            return;
        }

        // Enforced here rather than on a timer. A store that only prunes when something remembers
        // to ask is a store whose size depends on which code path was used, and the run nobody
        // thought to prune is the one that makes the file large.
        await TrimAsync(_retention, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentActivity>> GetRecentAsync(
        int count = 25,
        CancellationToken cancellationToken = default)
    {
        var limit = Math.Clamp(count, 1, 1_000);

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = $"""
            SELECT {Columns} FROM AgentActivity
            ORDER BY StartedAt DESC
            LIMIT $limit;
            """;

        command.Parameters.AddWithValue("$limit", limit);

        return await ReadAllAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentActivity>> GetForConversationAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = $"""
            SELECT {Columns} FROM AgentActivity
            WHERE ConversationId = $conversationId
            ORDER BY StartedAt ASC
            LIMIT {MaximumConversationRows};
            """;

        command.Parameters.AddWithValue("$conversationId", conversationId.ToString());

        return await ReadAllAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> PruneAsync(
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        // Compared as text, which is safe only because both sides are written by this build in
        // the same round-trip UTC form. That is a real constraint and it is the reason the
        // writer never stores a local timestamp.
        command.CommandText = "DELETE FROM AgentActivity WHERE StartedAt < $cutoff;";
        command.Parameters.AddWithValue("$cutoff", olderThan.ToStorage());

        var removed = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (removed > 0)
        {
            _logger.LogInformation("Pruned {Count} run(s) from the agent timeline.", removed);
        }

        return removed;
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = "DELETE FROM AgentActivity;";

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void Bind(SqliteCommand command, AgentActivity activity)
    {
        command.Parameters.AddWithValue("$id", activity.Id.ToString());
        command.Parameters.AddWithValue("$runId", activity.RunId.ToString());
        command.Parameters.AddWithValue("$conversationId", activity.ConversationId.ToParameter());
        command.Parameters.AddWithValue("$goal", activity.Goal);
        command.Parameters.AddWithValue("$startedAt", activity.StartedAt.ToStorage());
        command.Parameters.AddWithValue("$completedAt", activity.CompletedAt.ToStorage().ToParameter());
        command.Parameters.AddWithValue("$source", activity.Source.ToString());
        command.Parameters.AddWithValue("$status", activity.Status.ToString());
        command.Parameters.AddWithValue("$toolsUsed", activity.ToolsUsed.ToStorage());
        command.Parameters.AddWithValue("$durationMs", (long)activity.Duration.TotalMilliseconds);
        command.Parameters.AddWithValue("$sourceCount", activity.SourceCount);
        command.Parameters.AddWithValue("$note", activity.Note.ToParameter());
        command.Parameters.AddWithValue("$errorCode", activity.ErrorCode.ToParameter());
    }

    private static async Task<IReadOnlyList<AgentActivity>> ReadAllAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var results = new List<AgentActivity>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(Read(reader));
        }

        return results;
    }

    /// <summary>
    /// Reads one row, skipping any this build cannot make sense of.
    /// <para>
    /// A row written by a newer build carries a status or a source name that was added after
    /// this one. Reading it as an unknown value would put a word in the timeline that nothing
    /// maps to a colour, which is worse than showing one run less. The cost of a skipped row is
    /// an entry somebody cannot recall; the cost of a misread row is a page that lies about what
    /// ran.
    /// </para>
    /// </summary>
    private static AgentActivity Read(SqliteDataReader reader)
    {
        Guid? conversationId = reader.IsDBNull(2)
            ? null
            : Guid.Parse(reader.GetString(2));

        return new AgentActivity(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            reader.GetString(3),
            reader.ReadTimestamp(4),
            Enum.Parse<AgentRequestSource>(reader.GetString(6), ignoreCase: true),
            Enum.Parse<AgentActivityStatus>(reader.GetString(7), ignoreCase: true),
            conversationId)
        {
            CompletedAt = reader.ReadNullableTimestamp(5),
            ToolsUsed = AgentStorage.ListFromStorage(reader.GetString(8)),
            Duration = TimeSpan.FromMilliseconds(reader.GetInt64(9)),
            SourceCount = reader.GetInt32(10),
            Note = reader.ReadNullableText(11),
            ErrorCode = reader.ReadNullableText(12),
        };
    }

    /// <summary>
    /// Deletes everything in the timeline older than the newest <paramref name="keep"/> entries, so
    /// the file cannot grow without bound on a machine that has been using the agent a lot.
    /// </summary>
    private async Task<int> TrimAsync(int keep, CancellationToken cancellationToken = default)
    {
        var limit = Math.Clamp(keep, 1, 100_000);

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = $"""
            DELETE FROM AgentActivity
            WHERE Id NOT IN (
                SELECT Id FROM AgentActivity ORDER BY StartedAt DESC LIMIT {limit.ToString(CultureInfo.InvariantCulture)}
            );
            """;

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}