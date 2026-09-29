using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Knowledge;
using WindowsAIAssistant.Core.Abstractions.Time;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Embeddings;
using WindowsAIAssistant.Core.Models.Knowledge;

namespace WindowsAIAssistant.Infrastructure.Knowledge;

/// <summary>
/// Keeps knowledge documents in the local index, and is the authority on duplicates and changes.
/// <para>
/// Both of those questions are answered from the stored content hash rather than from the file's
/// name, its folder, or its timestamp. A name is something a person can type, so a name is
/// wrong about duplicates as soon as a file is copied, and wrong about changes as soon as it is
/// renamed. The hash is a fact about the bytes.
/// </para>
/// <para>
/// The hash column carries a uniqueness constraint in the schema, and this class relies on it
/// rather than checking first. A check followed by an insert is a race with a second process, a
/// second question, or a person pressing the button twice; a constraint is not, because the
/// database itself refuses the second write. The exception that comes back is caught and turned
/// into the existing document, so the duplicate check and the write are one operation rather
/// than two.
/// </para>
/// </summary>
public sealed class SqliteKnowledgeDocumentRepository : IKnowledgeDocumentRepository
{
    private readonly KnowledgeDatabase _database;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<SqliteKnowledgeDocumentRepository> _logger;

    public SqliteKnowledgeDocumentRepository(
        KnowledgeDatabase database,
        IDateTimeProvider clock,
        ILogger<SqliteKnowledgeDocumentRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _database = database;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KnowledgeDocument>> ListAsync(
        Guid knowledgeBaseId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = $"""
            SELECT {KnowledgeRow.DocumentColumns}
            FROM Documents
            WHERE KnowledgeBaseId = $id
            ORDER BY AddedAt DESC, FileName ASC;
            """;

        command.Parameters.AddWithValue("$id", knowledgeBaseId.ToString());

        var results = new List<KnowledgeDocument>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(reader.ReadDocument());
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<KnowledgeDocument?> GetAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = $"""
            SELECT {KnowledgeRow.DocumentColumns} FROM Documents WHERE Id = $id LIMIT 1;
            """;

        command.Parameters.AddWithValue("$id", documentId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? reader.ReadDocument() : null;
    }

    /// <inheritdoc />
    public async Task<KnowledgeDocument?> FindByHashAsync(string fileHash, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileHash))
        {
            return null;
        }

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        // Across the whole index rather than within one base, so adding a paper to "Research"
        // that is already in "University" says so instead of quietly producing the same answer
        // back twice from two sources.
        command.CommandText = $"""
            SELECT {KnowledgeRow.DocumentColumns} FROM Documents WHERE FileHash = $hash LIMIT 1;
            """;

        command.Parameters.AddWithValue("$hash", fileHash);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? reader.ReadDocument() : null;
    }

    /// <inheritdoc />
    public async Task SaveAsync(KnowledgeDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        // An upsert on the identifier, and nothing about the hash. Saving a document that has
        // changed on disk is the same identifier with a different hash, and that is the ordinary
        // case after a reindex. A write that also asserted the hash would refuse exactly the
        // write it exists to perform.
        command.CommandText = $"""
            INSERT INTO Documents ({KnowledgeRow.DocumentColumns})
            VALUES ($id, $baseId, $fileName, $filePath, $fileType, $fileSize, $fileHash, $modifiedAt,
                    $indexedAt, $chunkCount, $status, $statusMessage, $provider, $model, $dimensions, $addedAt)
            ON CONFLICT (Id) DO UPDATE SET
                KnowledgeBaseId     = excluded.KnowledgeBaseId,
                FileName            = excluded.FileName,
                FilePath            = excluded.FilePath,
                FileType            = excluded.FileType,
                FileSize            = excluded.FileSize,
                FileHash            = excluded.FileHash,
                ModifiedAt          = excluded.ModifiedAt,
                IndexedAt           = excluded.IndexedAt,
                ChunkCount          = excluded.ChunkCount,
                Status              = excluded.Status,
                StatusMessage       = excluded.StatusMessage,
                EmbeddingProvider   = excluded.EmbeddingProvider,
                EmbeddingModel      = excluded.EmbeddingModel,
                EmbeddingDimensions = excluded.EmbeddingDimensions;
            """;

        AddDocumentParameters(command, document);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetStatusAsync(
        Guid documentId,
        KnowledgeDocumentStatus status,
        string? message = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        // IndexedAt is set on the way into the indexed state and not when it is left, so a
        // document marked as needing a reindex still says when it was last fully indexed rather
        // than when it was last touched.
        command.CommandText = """
            UPDATE Documents
            SET Status        = $status,
                StatusMessage = $message,
                IndexedAt     = CASE WHEN $status = $indexed THEN $now ELSE IndexedAt END
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue("$status", (int)status);
        command.Parameters.AddWithValue("$message", (object?)message ?? DBNull.Value);
        command.Parameters.AddWithValue("$indexed", (int)KnowledgeDocumentStatus.Indexed);
        command.Parameters.AddWithValue("$now", _clock.UtcNow.ToStorage());
        command.Parameters.AddWithValue("$id", documentId.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkOutdatedAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        // The note is set here, in the same statement, so a row cannot be marked outdated by one
        // path and left with another path's message.
        command.CommandText = """
            UPDATE Documents
            SET Status        = $outdated,
                StatusMessage = $message
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue("$outdated", (int)KnowledgeDocumentStatus.Outdated);
        command.Parameters.AddWithValue(
            "$message",
            "This file has changed on disk. Its indexed text is still searched until you reindex it.");
        command.Parameters.AddWithValue("$id", documentId.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> MarkReindexRequiredAsync(
        EmbeddingSpace? currentSpace,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        // One statement over the whole table rather than a document at a time, because the
        // decision to reindex is made by a model change that applies to every document equally.
        // A document that has never been embedded is left alone: it has no vectors to invalidate
        // and will embed itself when it is next indexed.
        command.CommandText = $"""
            UPDATE Documents
            SET Status        = $reindex,
                StatusMessage = $message
            WHERE EmbeddingDimensions IS NOT NULL
              AND (
                    $provider IS NULL
                 OR EmbeddingProvider <> $provider
                 OR EmbeddingModel    <> $model
                 OR EmbeddingDimensions <> $dimensions
              );
            """;

        command.Parameters.AddWithValue("$reindex", (int)KnowledgeDocumentStatus.ReindexRequired);
        command.Parameters.AddWithValue(
            "$message",
            "The embedding model has changed. Reindex this file to include it in answers again.");
        command.Parameters.AddWithValue("$provider", (object?)currentSpace?.Provider ?? DBNull.Value);
        command.Parameters.AddWithValue("$model", (object?)currentSpace?.Model ?? DBNull.Value);
        command.Parameters.AddWithValue("$dimensions", (object?)currentSpace?.Dimensions ?? DBNull.Value);

        var marked = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (marked > 0)
        {
            _logger.LogInformation(
                "{DocumentCount} indexed documents were marked as needing a reindex after the embedding model changed.",
                marked);
        }

        return marked;
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        string baseId;

        // Read first, so the base's counts can be corrected in the same transaction as the
        // removal. Doing it afterwards would leave a base claiming a document it no longer has
        // if the process stopped in between, and nothing would ever correct it.
        await using (var lookup = connection.CreateCommand())
        {
            lookup.Transaction = (SqliteTransaction)transaction;
            lookup.CommandText = "SELECT KnowledgeBaseId FROM Documents WHERE Id = $id LIMIT 1;";
            lookup.Parameters.AddWithValue("$id", documentId.ToString());

            var found = await lookup.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (found is null or DBNull)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            baseId = (string)found;
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;

            // The passages go with the document, through the cascade. They are not deleted by a
            // statement here, because a statement here would be one more place to forget.
            command.CommandText = "DELETE FROM Documents WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", documentId.ToString());

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = RefreshCountsScript;
            command.Parameters.AddWithValue("$id", baseId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        // No path here touches the file on disk. Removing a document from a knowledge base is a
        // statement about the index, and the file is where the person put it.
        _logger.LogInformation("A document was removed from the knowledge index. The original file was not touched.");
        return true;
    }

    /// <summary>
    /// The count correction, written once because two statements needing it is how the two
    /// copies drift apart.
    /// </summary>
    internal const string RefreshCountsScript = """
        UPDATE KnowledgeBases
        SET DocumentCount = (SELECT COUNT(*) FROM Documents WHERE KnowledgeBaseId = $id),
            ChunkCount    = (SELECT COUNT(*) FROM Chunks WHERE KnowledgeBaseId = $id)
        WHERE Id = $id;
        """;

    private static void AddDocumentParameters(SqliteCommand command, KnowledgeDocument document)
    {
        command.Parameters.AddWithValue("$id", document.Id.ToString());
        command.Parameters.AddWithValue("$baseId", document.KnowledgeBaseId.ToString());
        command.Parameters.AddWithValue("$fileName", document.FileName);
        command.Parameters.AddWithValue("$filePath", document.FilePath);
        command.Parameters.AddWithValue("$fileType", (int)document.FileType);
        command.Parameters.AddWithValue("$fileSize", document.FileSize);
        command.Parameters.AddWithValue("$fileHash", document.FileHash);
        command.Parameters.AddWithValue("$modifiedAt", document.ModifiedAt.ToStorage() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$indexedAt", document.IndexedAt.ToStorage() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$chunkCount", document.ChunkCount);
        command.Parameters.AddWithValue("$status", (int)document.Status);
        command.Parameters.AddWithValue("$statusMessage", (object?)document.StatusMessage ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$provider", (object?)document.EmbeddingSpace?.Provider ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$model", (object?)document.EmbeddingSpace?.Model ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$dimensions", (object?)document.EmbeddingSpace?.Dimensions ?? DBNull.Value);
        command.Parameters.AddWithValue("$addedAt", document.AddedAt.ToStorage());
    }
}
