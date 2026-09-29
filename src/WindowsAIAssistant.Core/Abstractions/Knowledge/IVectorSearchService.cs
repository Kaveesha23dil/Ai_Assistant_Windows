using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Embeddings;
using WindowsAIAssistant.Core.Models.Knowledge;

namespace WindowsAIAssistant.Core.Abstractions.Knowledge;

/// <summary>
/// Finds the passages in a knowledge base that point the same way as a question.
/// <para>
/// The seam that lets the storage be replaced. Today it is SQLite plus cosine similarity, which
/// is the right amount of machinery for a personal knowledge base on one machine. Tomorrow it
/// could be a SQLite vector extension, Qdrant, or pgvector, behind this same interface, with the
/// Application layer none the wiser — which is why the retriever is written against this and
/// never against a table.
/// </para>
/// <para>
/// The contract is deliberately narrow: a vector, a base, and a count in, scored passages out. It
/// does not know about prompts, models, or answers, and it must not, because a search that
/// started composing prompts would be doing the answer's job and would have to be replaced along
/// with it.
/// </para>
/// </summary>
public interface IVectorSearchService
{
    /// <summary>
    /// Gets a value indicating whether the search can run, which is not the same as the store
    /// being reachable: a store with no vectors in it is available.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Gets a value indicating whether the store is temporarily unable to answer, because the
    /// service is not running or the network is down. A caller offering a degraded search needs
    /// to tell this from "there is nothing to find".
    /// </summary>
    bool IsTemporarilyUnavailable { get; }

    /// <summary>
    /// Finds the passages closest to a query vector.
    /// <summary>
    /// <param name="knowledgeBaseId">The base to search.</param>
    /// <param name="queryVector">The question's vector.</param>
    /// <param name="count">How many to return, before the caller applies its own threshold.</param>
    /// <param name="documentIds">Restrict to these documents, or empty for the whole base.</param>
    /// <param name="fileTypes">Restrict to these document families, or empty for all of them.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <exception cref="Exceptions.KnowledgeException">
    /// Thrown when the stored vectors are from a different embedding model than the query's, so
    /// that the person is told to reindex rather than being handed a ranking built from
    /// arithmetic that means nothing.
    /// </exception>
    Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(
        Guid knowledgeBaseId,
        EmbeddingVector queryVector,
        int count,
        IReadOnlyCollection<Guid>? documentIds = null,
        IReadOnlyCollection<DocumentFileType>? fileTypes = null,
        CancellationToken cancellationToken = default);
}
