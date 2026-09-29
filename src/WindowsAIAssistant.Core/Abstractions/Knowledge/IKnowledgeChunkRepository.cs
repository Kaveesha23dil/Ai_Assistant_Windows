using WindowsAIAssistant.Core.Models.Knowledge;

namespace WindowsAIAssistant.Core.Abstractions.Knowledge;

/// <summary>
/// Stores and reads the indexed passages of a document.
/// <para>
/// Everything here is scoped to a document rather than to a base. Reindexing replaces one
/// document's passages atomically — old out, new in — which is only expressible if the store
/// can be asked for exactly one document's passages, and it is the reason a reindex cannot leave
/// a document holding both the old text and the new.
/// </para>
/// </summary>
public interface IKnowledgeChunkRepository
{
    /// <summary>Lists the passages of a document, in reading order.</summary>
    Task<IReadOnlyList<KnowledgeChunk>> ListAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the passages of a base that carry a vector, for a scan that ranks every one of them.
    /// <para>
    /// The vectors only, not the text: this is the pass that scores candidates, and a base of a
    /// few thousand chunks would otherwise be read into memory as text as well and then thrown
    /// away. The text is read afterwards, for the handful that survived.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<IndexedChunkVector>> ListVectorsAsync(
        Guid knowledgeBaseId,
        CancellationToken cancellationToken = default);

    /// <summary>Reads back the text of a set of passages, keyed by identifier.</summary>
    Task<IReadOnlyDictionary<Guid, KnowledgeChunk>> GetManyAsync(
        IReadOnlyCollection<Guid> chunkIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a document's passages with the ones given, in one transaction.
    /// <para>
    /// Atomic is the requirement, not an optimization. An interrupted reindex that removed the
    /// old passages and failed before adding the new ones would leave a document marked indexed
    /// with nothing behind it, and a search would answer from a document that has none.
    /// </para>
    /// </summary>
    Task ReplaceForDocumentAsync(
        Guid documentId,
        IReadOnlyList<KnowledgeChunk> chunks,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up stored vectors for a set of content hashes, so a reindex can reuse the ones
    /// whose text has not changed instead of paying for them again.
    /// </summary>
    Task<IReadOnlyDictionary<string, KnowledgeChunk>> FindByContentHashesAsync(
        IReadOnlyCollection<string> contentHashes,
        CancellationToken cancellationToken = default);

    /// <summary>Counts a document's passages.</summary>
    Task<int> CountAsync(Guid documentId, CancellationToken cancellationToken = default);
}

/// <summary>
/// A stored passage reduced to what a scoring pass needs: its identity, where it is, and its
/// vector.
/// <para>
/// Separate from <see cref="KnowledgeChunk"/> because reading a few thousand of these should
/// not pull a few thousand passages' worth of document text into memory to throw it away. The
/// text is fetched afterwards for the few that pass, through
/// <see cref="IKnowledgeChunkRepository.GetMany"/>.
/// </para>
/// </summary>
/// <param name="ChunkId">The passage's identifier.</param>
/// <param name="DocumentId">The document it came from.</param>
/// <param name="Sequence">Its place in the document.</param>
/// <param name="FileName">The file's name, for a citation and for a filename filter.</param>
/// <param name="FileType">The document family, for a type filter.</param>
/// <param name="Embedding">The stored vector.</param>
public sealed record IndexedChunkVector(
    Guid ChunkId,
    Guid DocumentId,
    int Sequence,
    string FileName,
    Core.Enums.DocumentFileType FileType,
    Core.Models.Embeddings.EmbeddingVector Embedding);
