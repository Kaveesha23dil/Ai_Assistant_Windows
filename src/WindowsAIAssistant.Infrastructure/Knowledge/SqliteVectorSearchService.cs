using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.Embeddings;
using WindowsAIAssistant.Core.Abstractions.Knowledge;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models.Embeddings;
using WindowsAIAssistant.Core.Models.Knowledge;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.Knowledge;

/// <summary>
/// Finds the passages in a base whose vectors point the same way as a question's, by comparing
/// every one of them.
/// <para>
/// This is an exact scan, and that is a deliberate choice rather than a missing feature. A vector
/// index such as sqlite-vec or Qdrant would answer a nearest-neighbour query without reading
/// every vector, and at a few hundred thousand passages that is the difference between
/// milliseconds and seconds. At the scale this application actually reaches — a person's own
/// documents, indexed one folder at a time — the exact answer is both fast enough and exactly
/// right, and it has no approximation, no tuning, and no index to fall out of step with the data.
/// It is also a few dozen lines, where an extension is a dependency, a set of pragmas, and a
/// decision about which one to require of a machine that does not have it.
/// </para>
/// <para>
/// The seam is <see cref="IVectorSearchService"/> rather than this class, so replacing the scan
/// with a real index is a new implementation and no change to the retriever. What is written here
/// is the part that would be the same either way: the space check, the filters, and the ranking.
/// </para>
/// <para>
/// The scan is bounded by <see cref="KnowledgeBaseOptions.MaximumScannedChunks"/>. Reading every
/// vector of a large base for one question would grow without limit and would eventually stop
/// answering at all, so the scan takes the most recently indexed passages, which is where the
/// material somebody has been working with actually is.
/// </para>
/// </summary>
public sealed class SqliteVectorSearchService : IVectorSearchService
{
    private readonly IKnowledgeChunkRepository _chunks;
    private readonly IEmbeddingService _embeddings;
    private readonly ILogger<SqliteVectorSearchService> _logger;
    private readonly KnowledgeBaseOptions _options;

    public SqliteVectorSearchService(
        IKnowledgeChunkRepository chunks,
        IEmbeddingService embeddings,
        IOptions<KnowledgeBaseOptions> options,
        ILogger<SqliteVectorSearchService> logger)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(embeddings);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _chunks = chunks;
        _embeddings = embeddings;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public bool IsTemporarilyUnavailable => false;

    /// <inheritdoc />
    public async Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(
        Guid knowledgeBaseId,
        EmbeddingVector queryVector,
        int count,
        IReadOnlyCollection<Guid>? documentIds = null,
        IReadOnlyCollection<DocumentFileType>? fileTypes = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queryVector);

        if (count <= 0)
        {
            return [];
        }

        var indexed = await _chunks.ListVectorsAsync(knowledgeBaseId, cancellationToken).ConfigureAwait(false);

        if (indexed.Count == 0)
        {
            return [];
        }

        var space = queryVector.Space;
        var mismatched = 0;
        var candidates = new List<IndexedChunkVector>(indexed.Count);
        var filtered = ApplyFilters(indexed, documentIds, fileTypes, ref mismatched);

        // The scan is capped, and the cap is reported. A base with more passages than the cap
        // gives a person a search that looks complete and is not, which is worth a log line and
        // a comment even though neither is visible in the result.
        if (filtered.Count > _options.MaximumScannedChunks)
        {
            _logger.LogInformation(
                "The knowledge base holds more passages than one search reads, so the search covered {Scanned} of {Total}.",
                _options.MaximumScannedChunks,
                filtered.Count);

            filtered = filtered.TakeLast(_options.MaximumScannedChunks).ToList();
        }

        foreach (var candidate in filtered)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!candidate.Embedding.Space.IsCompatibleWith(space))
            {
                // Counted, not thrown on. One document indexed with an older model must not stop
                // a question being answered from the documents that are current; the count is
                // what tells the caller there is something to reindex.
                mismatched++;
                continue;
            }

            candidates.Add(candidate);
        }

        if (candidates.Count == 0 && mismatched > 0)
        {
            throw new KnowledgeException(
                "The documents in this knowledge base were indexed with a different embedding model. "
                    + "Reindex them to ask questions again.",
                ErrorCodes.KnowledgeEmbeddingSpaceMismatch);
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        var scored = new List<(IndexedChunkVector Chunk, double Score)>(candidates.Count);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Both sides are unit length, so the cosine similarity is the dot product and the
            // division that would otherwise be per comparison is not needed. The full
            // implementation is still used rather than an open-coded dot product, because it is
            // the one place the zero-vector and dimension checks live.
            var score = CosineSimilarity.Compute(candidate.Embedding, queryVector);
            scored.Add((candidate, score));
        }

        var best = scored
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Chunk.ChunkId)
            .Take(count)
            .ToArray();

        if (best.Length == 0)
        {
            return [];
        }

        // The text is read only for the passages that won. A scan of several thousand vectors
        // followed by reading several thousand passages' worth of text would hold a whole
        // knowledge base in memory to answer one question, and would throw nearly all of it away.
        var texts = await _chunks.GetManyAsync(
            best.Select(entry => entry.Chunk.ChunkId).ToArray(),
            cancellationToken).ConfigureAwait(false);

        var results = new List<KnowledgeSearchResult>(best.Length);

        foreach (var (candidate, score) in best)
        {
            if (!texts.TryGetValue(candidate.ChunkId, out var chunk))
            {
                continue;
            }

            results.Add(new KnowledgeSearchResult
            {
                ChunkId = candidate.ChunkId,
                DocumentId = candidate.DocumentId,
                FileName = candidate.FileName,
                Sequence = candidate.Sequence,
                Text = chunk.Text,
                Sections = chunk.Sections,
                VectorScore = score,
                SourceReference = chunk.SourceReference,
                Space = candidate.Embedding.Space,
            });
        }

        return results;
    }

    /// <summary>
    /// Narrows the scan to what the question asked about.
    /// <para>
    /// Done here, in memory, over the rows the repository already returned, rather than pushed
    /// into SQL as a second query. The scan is exact and must read every vector anyway, so a
    /// filter that arrives as a predicate would avoid scoring without avoiding the read — and it
    /// would make the read itself conditional on a caller having remembered to pass the filter,
    /// which is the mistake the query type was written to make impossible.
    /// </para>
    /// </summary>
    private static IReadOnlyList<IndexedChunkVector> ApplyFilters(
        IReadOnlyList<IndexedChunkVector> indexed,
        IReadOnlyCollection<Guid>? documentIds,
        IReadOnlyCollection<DocumentFileType>? fileTypes,
        ref int excluded)
    {
        if (documentIds is null or { Count: 0 } && fileTypes is null or { Count: 0 })
        {
            return indexed;
        }

        var documentSet = documentIds is { Count: > 0 } ? documentIds.ToHashSet() : null;
        var typeSet = fileTypes is { Count: > 0 } ? fileTypes.ToHashSet() : null;

        var filtered = new List<IndexedChunkVector>(indexed.Count);

        foreach (var candidate in indexed)
        {
            if (documentSet is not null && !documentSet.Contains(candidate.DocumentId))
            {
                excluded++;
                continue;
            }

            if (typeSet is not null && !typeSet.Contains(candidate.FileType))
            {
                excluded++;
                continue;
            }

            filtered.Add(candidate);
        }

        return filtered;
    }
}
