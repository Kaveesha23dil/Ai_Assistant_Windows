using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Knowledge;
using WindowsAIAssistant.Core.Abstractions.Time;
using WindowsAIAssistant.Core.Models.Knowledge;

namespace WindowsAIAssistant.Infrastructure.Knowledge;

/// <summary>
/// Keeps knowledge bases in the local index.
/// <para>
/// A repository rather than a service, and stateless: it opens a connection for each call and
/// closes it again, so the page, a voice question, and a background reindex can all be working
/// with different bases at the same time without any of them holding a lock for the others.
/// </para>
/// <para>
/// The counts on a base are maintained by this class rather than trusted from a caller, because
/// they are derived from the documents and a document count that nobody counted is a number
/// that will be wrong. They are recomputed from the document table on every change, which is one
/// cheap aggregate over an index that fits on a desktop, and it means a count cannot drift after
/// an interrupted reindex.
/// </para>
/// </summary>
public sealed class SqliteKnowledgeBaseRepository : IKnowledgeBaseRepository
{
    private const string Columns =
        "Id, Name, Description, DocumentCount, ChunkCount, IsDefault, CreatedAt, UpdatedAt";

    private readonly KnowledgeDatabase _database;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<SqliteKnowledgeBaseRepository> _logger;

    public SqliteKnowledgeBaseRepository(
        KnowledgeDatabase database,
        IDateTimeProvider clock,
        ILogger<SqliteKnowledgeBaseRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _database = database;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<KnowledgeBase> GetOrCreateDefaultAsync(CancellationToken cancellationToken = default)
    {
        var existing = await FindDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        return await CreateDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KnowledgeBase>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = $"SELECT {Columns} FROM KnowledgeBases ORDER BY CreatedAt ASC, Name ASC;";

        var results = new List<KnowledgeBase>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(ReadBase(reader));
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<KnowledgeBase?> GetAsync(Guid knowledgeBaseId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = $"SELECT {Columns} FROM KnowledgeBases WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", knowledgeBaseId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadBase(reader) : null;
    }

    /// <inheritdoc />
    public async Task<KnowledgeBase> AddAsync(KnowledgeBase knowledgeBase, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(knowledgeBase);

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;

        command.CommandText = $"""
            INSERT INTO KnowledgeBases ({Columns})
            VALUES ($id, $name, $description, 0, 0, $isDefault, $createdAt, $updatedAt);
            """;

        command.Parameters.AddWithValue("$id", knowledgeBase.Id.ToString());
        command.Parameters.AddWithValue("$name", knowledgeBase.Name);
        command.Parameters.AddWithValue("$description", (object?)knowledgeBase.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$isDefault", knowledgeBase.IsDefault ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", knowledgeBase.CreatedAt.ToStorage());
        command.Parameters.AddWithValue("$updatedAt", knowledgeBase.UpdatedAt.ToStorage());

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return knowledgeBase with { DocumentCount = 0, ChunkCount = 0 };
    }

    /// <inheritdoc />
    public async Task RenameAsync(
        Guid knowledgeBaseId,
        string name,
        string? description,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        // UpdatedAt is written here and not taken from the caller, for the same reason the
        // counts are recomputed: both are facts about the row rather than requests about it.
        command.CommandText = $"""
            UPDATE KnowledgeBases
            SET Name = $name, Description = $description, UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue("$name", name.Trim());
        command.Parameters.AddWithValue("$description", (object?)description?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", _clock.UtcNow.ToStorage());
        command.Parameters.AddWithValue("$id", knowledgeBaseId.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid knowledgeBaseId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        // The document and passage rows go with it, through the cascades declared in the schema
        // rather than through deletes issued here. One declaration covers every path that removes
        // a base, including ones added later.
        command.CommandText = "DELETE FROM KnowledgeBases WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", knowledgeBaseId.ToString());

        var removed = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (removed > 0)
        {
            _logger.LogInformation("A knowledge base and everything indexed in it were removed.");
        }

        return removed > 0;
    }

    /// <inheritdoc />
    public async Task RefreshCountsAsync(Guid knowledgeBaseId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        // One aggregate over both tables rather than a count per document, and restricted to the
        // base being refreshed. A document's passages are counted whether or not the document is
        // currently searchable, so an interrupted reindex cannot leave a base claiming a
        // different number from the one on disk.
        command.CommandText = """
            UPDATE KnowledgeBases
            SET DocumentCount = (
                    SELECT COUNT(*) FROM Documents WHERE KnowledgeBaseId = $id
                ),
                ChunkCount = (
                    SELECT COUNT(*) FROM Chunks WHERE KnowledgeBaseId = $id
                ),
                UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue("$id", knowledgeBaseId.ToString());
        command.Parameters.AddWithValue("$updatedAt", _clock.UtcNow.ToStorage());

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<KnowledgeBase?> FindDefaultAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = $"SELECT {Columns} FROM KnowledgeBases WHERE IsDefault = 1 LIMIT 1;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadBase(reader) : null;
    }

    /// <summary>
    /// Creates the base a question lands on when none was named, and promotes it to default in
    /// the same transaction.
    /// </summary>
    /// <remarks>
    /// The whole thing is one transaction because "no default exists" and "a default exists" is
    /// the race two questions asked at the same moment would otherwise both observe. The second
    /// one to commit finds the unique index has already been satisfied, and returns the base the
    /// first created instead of failing the person's question.
    /// </remarks>
    private async Task<KnowledgeBase> CreateDefaultAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var created = KnowledgeBase.Create(
            KnowledgeDatabase.DefaultKnowledgeBaseName,
            now,
            "The documents you have added to the assistant.",
            isDefault: true);

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);

        // Re-read inside the transaction. The check before this one was made on a different
        // connection, and between the two something else may have created it.
        await using (var lookup = connection.CreateCommand())
        {
            lookup.Transaction = transaction;
            lookup.CommandText = $"SELECT {Columns} FROM KnowledgeBases WHERE IsDefault = 1 LIMIT 1;";

            await using var reader = await lookup.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return ReadBase(reader);
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = $"""
                INSERT INTO KnowledgeBases ({Columns})
                VALUES ($id, $name, $description, 0, 0, 1, $createdAt, $updatedAt);
                """;

            command.Parameters.AddWithValue("$id", created.Id.ToString());
            command.Parameters.AddWithValue("$name", created.Name);
            command.Parameters.AddWithValue("$description", (object?)created.Description ?? DBNull.Value);
            command.Parameters.AddWithValue("$createdAt", created.CreatedAt.ToStorage());
            command.Parameters.AddWithValue("$updatedAt", created.UpdatedAt.ToStorage());

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return created;
    }

    private static KnowledgeBase ReadBase(SqliteDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        Name = reader.GetString(1),
        Description = reader.IsDBNull(2) ? null : reader.GetString(2),
        DocumentCount = reader.GetInt32(3),
        ChunkCount = reader.GetInt32(4),
        IsDefault = reader.GetInt32(5) == 1,
        CreatedAt = reader.GetTimestamp(6),
        UpdatedAt = reader.GetTimestamp(7),
    };
}
