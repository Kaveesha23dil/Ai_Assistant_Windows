using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Embeddings;
using WindowsAIAssistant.Core.Abstractions.Knowledge;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models.Embeddings;
using WindowsAIAssistant.Core.Models.Knowledge;

namespace WindowsAIAssistant.Application.Knowledge;

/// <summary>
/// Finds the passages worth answering a question from.
/// <para>
/// Retrieval in three steps, in this order: find the passages whose vectors point the same way,
/// find the ones that share the question's wording, and rank the two together. The vector step
/// is skipped only when it cannot be performed at all — no provider, no credential, a permission
/// withdrawn — and never skipped merely because it would be convenient, because a knowledge base
/// whose whole value is semantic search and which quietly degrades to keyword matching is worse
/// than one that says it could not do it.
/// </para>
/// <para>
/// The degraded path is a real feature rather than a fallback bolted on. Somebody whose embedding
/// permission is off, or who is offline with no local model, still gets answers from their own
/// documents, and the result says plainly that the passages were found by wording. What they do
/// not get is a search that claims to be semantic while being a keyword match.
/// </para>
/// <para>
/// The two halves are a union, not a re-score. A passage the vectors did not rank highly but
/// whose wording matches exactly is still the passage that answers the question, and a hybrid
/// search that only ever re-scores what meaning already found is a semantic search with a
/// tie-breaker. The union is ranked once, so the two halves are compared on the same scale
/// rather than merged after the fact.
/// </para>
/// </summary>
public sealed class RagRetriever : IRagRetriever
{
    private readonly IEmbeddingService _embeddings;
    private readonly IHybridSearchRanker _ranker;
    private readonly IKnowledgeChunkRepository _chunks;
    private readonly IKnowledgeDocumentRepository _documents;
    private readonly IVectorSearchService _vectors;
    private readonly KnowledgeProcessingLimits _limits;
    private readonly RagRetrievalPolicy _policy;
    private readonly ILogger<RagRetriever> _logger;

    public RagRetriever(
        IVectorSearchService vectors,
        IHybridSearchRanker ranker,
        IEmbeddingService embeddings,
        IKnowledgeChunkRepository chunks,
        IKnowledgeDocumentRepository documents,
        KnowledgeProcessingLimits limits,
        RagRetrievalPolicy policy,
        ILogger<RagRetriever> logger)
    {
        ArgumentNullException.ThrowIfNull(vectors);
        ArgumentNullException.ThrowIfNull(ranker);
        ArgumentNullException.ThrowIfNull(embeddings);
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(logger);

        _vectors = vectors;
        _ranker = ranker;
        _embeddings = embeddings;
        _chunks = chunks;
        _documents = documents;
        _limits = limits;
        _policy = policy;
        _logger = logger;
    }

    /// <inheritdoc />
    public EmbeddingSpace? CurrentSpace { get; private set; }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KnowledgeSearchResult>> RetrieveAsync(
        KnowledgeQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.LexicalOnly)
        {
            return await RetrieveLexicallyAsync(query, cancellationToken).ConfigureAwait(false);
        }

        EmbeddingVector queryVector;

        try
        {
            queryVector = await _embeddings.GenerateAsync(query.Question, cancellationToken).ConfigureAwait(false);
        }
        catch (KnowledgeException exception) when (IsDegradable(exception.ErrorCode))
        {
            // No provider, no credential, no permission, or the service could not be reached. The
            // question is still answerable from the documents by wording, so it is answered that
            // way and the caller is told how.
            _logger.LogInformation(
                "The question was answered by keyword search because embeddings were unavailable. {ErrorCode}",
                exception.ErrorCode);

            return await RetrieveLexicallyAsync(query, cancellationToken).ConfigureAwait(false);
        }

        CurrentSpace = queryVector.Space;

        var documents = await ResolveSearchableDocumentsAsync(query, cancellationToken).ConfigureAwait(false);

        if (documents.Count == 0)
        {
            return [];
        }

        // Enough candidates to fill the quota several times over, because the threshold, the
        // redundancy pass, and the per-document cap each remove some. Asking the store for
        // exactly as many as will be used and then discarding half of it would mean the answer is
        // built from whatever survived being under-supplied.
        var candidateCount = Math.Clamp(
            query.Count * _policy.CandidateMultiplier,
            _limits.DefaultRetrievalCount,
            _limits.MaximumRetrievalCount * _policy.CandidateMultiplier);

        var semantic = await _vectors.SearchAsync(
            query.KnowledgeBaseId,
            queryVector,
            candidateCount,
            [.. documents.Select(document => document.Id)],
            query.FileTypes.Count > 0 ? query.FileTypes : null,
            cancellationToken).ConfigureAwait(false);

        if (!_policy.UseHybridSearch)
        {
            return _ranker.Rank(semantic, query.Question, query.Count);
        }

        // The vector store returns no text, and text is the whole point of the wording half, so
        // the passages are read once by identifier. One read serves both halves: the ids come
        // from the vector scan and from the wording scan, and each passage is fetched at most
        // once however many of the two found it.
        var vectorScores = semantic.ToDictionary(result => result.ChunkId, result => result.VectorScore);

        var wordingIds = await FindWordingMatchesAsync(
            query,
            documents,
            candidateCount,
            cancellationToken).ConfigureAwait(false);

        var ids = new List<Guid>(vectorScores.Count + wordingIds.Count);

        foreach (var id in vectorScores.Keys)
        {
            ids.Add(id);
        }

        foreach (var id in wordingIds)
        {
            if (!vectorScores.ContainsKey(id))
            {
                ids.Add(id);
            }
        }

        if (ids.Count == 0)
        {
            return [];
        }

        var chunks = await _chunks.GetManyAsync([.. ids], cancellationToken).ConfigureAwait(false);

        var candidates = new List<KnowledgeSearchResult>(chunks.Count);

        foreach (var (chunkId, chunk) in chunks)
        {
            candidates.Add(Describe(chunk, documents, vectorScores.GetValueOrDefault(chunkId)));
        }

        return _ranker.Rank(candidates, query.Question, query.Count);
    }

    /// <summary>
    /// Builds the result for a passage, taking the file's name and family from the document the
    /// question was allowed to read rather than from the passage, which does not repeat them.
    /// </summary>
    private static KnowledgeSearchResult Describe(
        KnowledgeChunk chunk,
        IReadOnlyList<KnowledgeDocument> documents,
        double vectorScore)
    {
        var document = documents.FirstOrDefault(candidate => candidate.Id == chunk.DocumentId);

        return new KnowledgeSearchResult
        {
            ChunkId = chunk.Id,
            DocumentId = chunk.DocumentId,
            KnowledgeBaseId = chunk.KnowledgeBaseId,
            FileName = document?.FileName ?? string.Empty,
            FileType = document?.FileType ?? DocumentFileType.Unknown,
            Sequence = chunk.Sequence,
            Text = chunk.Text,
            Sections = chunk.Sections,
            SourceReference = chunk.SourceReference,
            Space = chunk.Embedding?.Space,
            VectorScore = vectorScore,
        };
    }

    /// <summary>
    /// Finds passages by wording only.
    /// <para>
    /// The whole base is read, because there is no way to know in advance which passages contain
    /// the words. That is affordable here and only here: the degraded path runs when embeddings
    /// are unavailable, which for a personal knowledge base is a base of a few thousand passages
    /// rather than a corpus, and the alternative is refusing to answer at all.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<KnowledgeSearchResult>> RetrieveLexicallyAsync(
        KnowledgeQuery query,
        CancellationToken cancellationToken)
    {
        var documents = await ResolveSearchableDocumentsAsync(query, cancellationToken).ConfigureAwait(false);

        if (documents.Count == 0)
        {
            return [];
        }

        var ids = await FindWordingMatchesAsync(query, documents, query.Count, cancellationToken)
            .ConfigureAwait(false);

        if (ids.Count == 0)
        {
            return [];
        }

        var chunks = await _chunks.GetManyAsync([.. ids], cancellationToken).ConfigureAwait(false);

        var candidates = new List<KnowledgeSearchResult>(chunks.Count);

        foreach (var (chunkId, chunk) in chunks)
        {
            candidates.Add(Describe(chunk, documents, double.NaN));
        }

        return LexicalSearch.Find(query.Question, candidates, query.Count, LexicalSearch.DefaultMinimumScore);
    }

    /// <summary>
    /// Scans the base for the passages that share the question's wording.
    /// <para>
    /// The scan reads the vector index rather than the passages, because the index row already
    /// carries the document and file name a filter needs, and the passages themselves are then
    /// read only for those that are scored. It is the one part of retrieval that scales with the
    /// size of the base, which is what the scanned-chunk cap exists to bound.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<Guid>> FindWordingMatchesAsync(
        KnowledgeQuery query,
        IReadOnlyList<KnowledgeDocument> documents,
        int take,
        CancellationToken cancellationToken)
    {
        var searchable = documents.Select(document => document.Id).ToHashSet();

        var indexed = await _chunks.ListVectorsAsync(query.KnowledgeBaseId, cancellationToken).ConfigureAwait(false);

        var candidates = new List<KnowledgeSearchResult>(indexed.Count);

        foreach (var row in indexed)
        {
            if (searchable.Contains(row.DocumentId) && MatchesFilters(row, query))
            {
                candidates.Add(Describe(
                    new KnowledgeChunk
                    {
                        Id = row.ChunkId,
                        DocumentId = row.DocumentId,
                        KnowledgeBaseId = query.KnowledgeBaseId,
                        Sequence = row.Sequence,
                        Text = string.Empty,
                        ContentHash = string.Empty,
                        Embedding = row.Embedding,
                    },
                    documents,
                    double.NaN));
            }
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        // The wording pass needs text, and the text is in the passage, so the capped candidate set
        // is read once and scored locally. Reading only some of them would mean choosing which by
        // a criterion other than relevance.
        var scanned = candidates.Count > _limits.MaximumScannedChunks
            ? [.. candidates.TakeLast(_limits.MaximumScannedChunks)]
            : candidates;

        var chunks = await _chunks.GetManyAsync(
            [.. scanned.Select(candidate => candidate.ChunkId)],
            cancellationToken).ConfigureAwait(false);

        // Keyed rather than searched: a scan of a large base reads thousands of passages, and
        // looking each one up through a list would turn a bounded read into a quadratic one.
        var byId = scanned.ToDictionary(candidate => candidate.ChunkId);
        var withText = new List<KnowledgeSearchResult>(chunks.Count);

        foreach (var (chunkId, chunk) in chunks)
        {
            if (byId.TryGetValue(chunkId, out var candidate))
            {
                withText.Add(candidate with { Text = chunk.Text });
            }
        }

        return
        [
            .. LexicalSearch
                .Find(query.Question, withText, take, LexicalSearch.DefaultMinimumScore)
                .Select(result => result.ChunkId),
        ];
    }

    private static bool MatchesFilters(IndexedChunkVector row, KnowledgeQuery query)
    {
        if (query.FileNames.Count > 0
            && !query.FileNames.Contains(row.FileName, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return query.FileTypes.Count == 0 || query.FileTypes.Contains(row.FileType);
    }

    /// <summary>
    /// Resolves the documents a question may read, which is every searchable document in the base
    /// unless the question named some.
    /// <para>
    /// Documents that are not searchable are excluded here rather than in the scan, so a document
    /// that is being reindexed contributes nothing to any half of the ranking. It is also why a
    /// document needing a reindex after a model change is invisible to questions until it is
    /// reindexed: its vectors exist but cannot be compared, and pretending otherwise would be an
    /// answer built from arithmetic that means nothing.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<KnowledgeDocument>> ResolveSearchableDocumentsAsync(
        KnowledgeQuery query,
        CancellationToken cancellationToken)
    {
        var all = await _documents.ListAsync(query.KnowledgeBaseId, cancellationToken).ConfigureAwait(false);

        if (query.DocumentIds.Count > 0)
        {
            var wanted = query.DocumentIds.ToHashSet();

            return
            [
                .. all
                    .Where(document => wanted.Contains(document.Id) && document.Status.IsSearchable())
                    .Where(document => MatchesNameFilter(document, query)),
            ];
        }

        return
        [
            .. all
                .Where(document => document.Status.IsSearchable())
                .Where(document => MatchesNameFilter(document, query)),
        ];
    }

    private static bool MatchesNameFilter(KnowledgeDocument document, KnowledgeQuery query) =>
        query.FileNames.Count == 0
        || query.FileNames.Contains(document.FileName, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether an embedding failure is one a keyword search can be substituted for.
    /// <para>
    /// A closed list, and deliberately not "any failure". A space mismatch is not degradable:
    /// the index holds vectors from a model that cannot be compared with the question, and
    /// answering by keyword from a base whose passages were all selected on a different
    /// criterion would be a different answer from the one the person asked for, offered without
    /// saying so.
    /// </para>
    /// </summary>
    private static bool IsDegradable(string? errorCode) => errorCode is
        ErrorCodes.KnowledgeEmbeddingPermissionDenied
        or ErrorCodes.KnowledgeEmbeddingUnavailable
        or ErrorCodes.AiCredentialMissing
        or ErrorCodes.AiNetworkFailure
        or ErrorCodes.AiTimedOut;
}
