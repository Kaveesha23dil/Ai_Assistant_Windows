using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Knowledge;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Embeddings;
using WindowsAIAssistant.Core.Models.Knowledge;

namespace WindowsAIAssistant.Infrastructure.Knowledge;

/// <summary>
/// Keeps the passages of the knowledge index, and their vectors.
/// <para>
/// The method that matters here is <see cref="ReplaceForDocumentAsync"/>, and it is one
/// transaction because the two things it does are opposites. Deleting a document's old passages
/// and inserting the new ones are both necessary and neither is sufficient; done in two
/// transactions there is a moment in between when the document has no passages at all, and a
/// question arriving in that moment would be answered from a document that appears indexed and
/// contains nothing. That window is not a rare race — it is as wide as the embedding of a whole
/// document if the delete is committed before the insert is prepared, which is exactly the order
/// that saves memory.
/// </para>
/// <para>
/// The vectors are written as blobs by the serializer rather than as a table of numbers, for the
/// same reason they are written in one column: a 1,536-value vector is 6,144 bytes, and 1,536
/// rows of a real column is not a representation of it that any search would want. Nothing in
/// this design needs to add up the components of a stored vector, so nothing is lost by keeping
/// it as a value.
/// </para>
/// </summary>
public sealed class SqliteKnowledgeChunkRepository : IKnowledgeChunkRepository
{
    private readonly IVectorSerializer _serializer;
    private readonly KnowledgeDatabase _database;
    private readonly ILogger<SqliteKnowledgeChunkRepository> _logger;

    public SqliteKnowledgeChunkRepository(
        KnowledgeDatabase database,
        IVectorSerializer serializer,
        ILogger<SqliteKnowledgeChunkRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(logger);

        _database = database;
        _serializer = serializer;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KnowledgeChunk>> ListAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = $"""
            SELECT {KnowledgeRow.ChunkColumns}
            FROM Chunks
            WHERE DocumentId = $id
            ORDER BY Sequence ASC;
            """;

        command.Parameters.AddWithValue("$id", documentId.ToString());

        var results = new List<KnowledgeChunk>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(ReadChunkWithVector(reader));
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IndexedChunkVector>> ListVectorsAsync(
        Guid knowledgeBaseId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        // One join rather than a query per document. The status filter is in the join rather
        // than after it, so a document that is being indexed contributes nothing to the scan
        // instead of contributing passages that are about to be replaced.
        command.CommandText = $"""
            SELECT c.Id, c.DocumentId, c.Sequence, d.FileName, d.FileType,
                   c.Vector, c.VectorIsNormalized,
                   c.EmbeddingProvider, c.EmbeddingModel, c.EmbeddingDimensions
            FROM Chunks AS c
            INNER JOIN Documents AS d ON d.Id = c.DocumentId
            WHERE c.KnowledgeBaseId = $id
              AND c.Vector IS NOT NULL
              AND d.Status IN ({IndexedStatus},{OutdatedStatus})
            ORDER BY c.DocumentId ASC, c.Sequence ASC;
            """;

        command.Parameters.AddWithValue("$id", knowledgeBaseId.ToString());

        var results = new List<IndexedChunkVector>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var space = reader.GetSpace(7, 8, 9);
            if (space is null || reader.IsDBNull(5))
            {
                continue;
            }

            var blob = (byte[])reader.GetValue(5);

            try
            {
                results.Add(new IndexedChunkVector(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    (DocumentFileType)reader.GetInt32(4),
                    _serializer.Deserialize(blob, space)));
            }
            catch (Core.Exceptions.KnowledgeException ex)
            {
                // One damaged row must not fail the whole question. It is reported, counted, and
                // skipped: a search that returned nothing because of a single bad blob would be
                // worse than a search that returned slightly less.
                _logger.LogWarning(
                    "A stored vector was skipped because it could not be read. The document needs reindexing. {ErrorCode}",
                    ex.ErrorCode);
            }
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, KnowledgeChunk>> GetManyAsync(
        IReadOnlyCollection<Guid> chunkIds,
        CancellationToken cancellationToken = default)
    {
        if (chunkIds.Count == 0)
        {
            return new Dictionary<Guid, KnowledgeChunk>();
        }

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        var ids = chunkIds.ToArray();

        var names = new List<string>(ids.Length);
        for (var index = 0; index < ids.Length; index++)
        {
            names.Add($"$id{index}");
        }

        command.CommandText = $"""
            SELECT {KnowledgeRow.ChunkColumns}
            FROM Chunks
            WHERE Id IN ({string.Join(", ", names)});
            """;

        for (var index = 0; index < ids.Length; index++)
        {
            command.Parameters.AddWithValue(names[index], ids[index].ToString());
        }

        var results = new Dictionary<Guid, KnowledgeChunk>(chunkIds.Count);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var chunk = ReadChunkWithVector(reader);
            results[chunk.Id] = chunk;
        }

        return results;
    }

    /// <inheritdoc />
    public async Task ReplaceForDocumentAsync(
        Guid documentId,
        IReadOnlyList<KnowledgeChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // The delete comes first because it is the cheap half, and the transaction is what makes
        // the order safe: nothing outside this connection can see the document without passages
        // until the commit, and if the insert fails the delete is rolled back with it.
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = (SqliteTransaction)transaction;
            delete.CommandText = "DELETE FROM Chunks WHERE DocumentId = $id;";
            delete.Parameters.AddWithValue("$id", documentId.ToString());
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var chunk in chunks)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = (SqliteTransaction)transaction;

            insert.CommandText = $"""
                INSERT INTO Chunks ({KnowledgeRow.ChunkColumns})
                VALUES ($id, $documentId, $baseId, $sequence, $content, $sections, $source,
                        $characterCount, $contentHash, $vector, $normalized,
                        $provider, $model, $dimensions);
                """;

            insert.Parameters.AddWithValue("$id", chunk.Id.ToString());
            insert.Parameters.AddWithValue("$documentId", chunk.DocumentId.ToString());
            insert.Parameters.AddWithValue("$baseId", chunk.KnowledgeBaseId.ToString());
            insert.Parameters.AddWithValue("$sequence", chunk.Sequence);
            insert.Parameters.AddWithValue("$content", chunk.Text);
            insert.Parameters.AddWithValue("$sections", chunk.Sections.ToStorage());
            insert.Parameters.AddWithValue("$source", chunk.SourceReference.ToStorageString());
            insert.Parameters.AddWithValue("$characterCount", chunk.CharacterCount);
            insert.Parameters.AddWithValue("$contentHash", chunk.ContentHash);

            if (chunk.Embedding is { } embedding)
            {
                insert.Parameters.AddWithValue("$vector", _serializer.Serialize(embedding));
                insert.Parameters.AddWithValue("$normalized", embedding.IsNormalized ? 1 : 0);
                insert.Parameters.AddWithValue("$provider", embedding.Provider);
                insert.Parameters.AddWithValue("$model", embedding.Model);
                insert.Parameters.AddWithValue("$dimensions", embedding.Dimensions);
            }
            else
            {
                insert.Parameters.AddWithValue("$vector", DBNull.Value);
                insert.Parameters.AddWithValue("$normalized", 0);
                insert.Parameters.AddWithValue("$provider", DBNull.Value);
                insert.Parameters.AddWithValue("$model", DBNull.Value);
                insert.Parameters.AddWithValue("$dimensions", DBNull.Value);
            }

            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "A document's index was replaced with {ChunkCount} passages in one transaction.",
            chunks.Count);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, KnowledgeChunk>> FindByContentHashesAsync(
        IReadOnlyCollection<string> contentHashes,
        CancellationToken cancellationToken = default)
    {
        if (contentHashes.Count == 0)
        {
            return new Dictionary<string, KnowledgeChunk>(StringComparer.OrdinalIgnoreCase);
        }

        // A chunked insert, because the parameter limit is real and a document with more
        // passages than the limit is a large but ordinary document. Batching keeps a 2,000-chunk
        // reindex working instead of failing on the way in.
        var results = new Dictionary<string, KnowledgeChunk>(StringComparer.OrdinalIgnoreCase);
        var batchSize = 500;

        for (var offset = 0; offset < contentHashes.Count; offset += batchSize)
        {
            var batch = contentHashes.Skip(offset).Take(batchSize).ToArray();
            if (batch.Length == 0)
            {
                continue;
            }

            await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();

            var names = new List<string>(batch.Length);
            for (var index = 0; index < batch.Length; index++)
            {
                names.Add($"$hash{index}");
            }

            command.CommandText = $"""
                SELECT {KnowledgeRow.ChunkColumns}
                FROM Chunks
                WHERE ContentHash IN ({string.Join(", ", names)});
                """;

            for (var index = 0; index < batch.Length; index++)
            {
                command.Parameters.AddWithValue(names[index], batch[index]);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var chunk = ReadChunkWithVector(reader);
                results[chunk.ContentHash] = chunk;
            }
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT COUNT(*) FROM Chunks WHERE DocumentId = $id;";
        command.Parameters.AddWithValue("$id", documentId.ToString());

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null or DBNull ? 0 : Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private KnowledgeChunk ReadChunkWithVector(SqliteDataReader reader)
    {
        var chunk = reader.ReadChunk();
        var space = reader.ReadChunkSpace();

        if (space is not null && !reader.IsDBNull(9))        {
            var blob = (byte[])reader.GetValue(9);

            try
            {
                return chunk.WithEmbedding(_serializer.Deserialize(blob, space));
            }
            catch (Core.Exceptions.KnowledgeException)
            {
                // The passage is still worth returning. A document whose vector could not be read
                // can be reindexed, and until then its text can still be found by wording.
                return chunk;
            }
        }

        return chunk;
    }

    private const int IndexedStatus = (int)KnowledgeDocumentStatus.Indexed;
    private const int OutdatedStatus = (int)KnowledgeDocumentStatus.Outdated;}
