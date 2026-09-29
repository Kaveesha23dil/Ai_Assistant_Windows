using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Embeddings;
using WindowsAIAssistant.Core.Models.Knowledge;

namespace WindowsAIAssistant.Core.Abstractions.Knowledge;

/// <summary>
/// Stores and reads the documents of a knowledge base.
/// <para>
/// The document row is the authority on two things nothing else can answer: whether a file with
/// this content has already been indexed, and whether a file on disk has changed since. Both
/// are content questions, so both are answered from the record's hash rather than from its name,
/// its folder, or its timestamp alone.
/// </para>
/// </summary>
public interface IKnowledgeDocumentRepository
{
    /// <summary>Lists the documents of a base, newest first.</summary>
    Task<IReadOnlyList<KnowledgeDocument>> ListAsync(
        Guid knowledgeBaseId,
        CancellationToken cancellationToken = default);

    /// <summary>Finds one document.</summary>
    Task<KnowledgeDocument?> GetAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a document by the hash of its contents, anywhere in the knowledge base.
    /// <para>
    /// This is the duplicate check. It searches on content rather than on path so that the same
    /// file under two names is recognised as one document, and it searches the whole index rather
    /// than one base so that a person adding a paper they already put in "Research" is told
    /// rather than being left to wonder why the same answer comes back twice from two sources.
    /// </para>
    /// </summary>
    Task<KnowledgeDocument?> FindByHashAsync(string fileHash, CancellationToken cancellationToken = default);

    /// <summary>Adds a document, or replaces the one already stored under the same identifier.</summary>
    Task SaveAsync(KnowledgeDocument document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a document to a new status, optionally with a note explaining it.
    /// </summary>
    Task SetStatusAsync(
        Guid documentId,
        KnowledgeDocumentStatus status,
        string? message = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a document was found to have changed on disk without reindexing it, so the
    /// page can show it as outdated.
    /// </summary>
    Task MarkOutdatedAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks every document whose vectors belong to a space other than the given one as needing a
    /// reindex, and returns how many were marked.
    /// <para>
    /// Applied to whole documents rather than to chunks, because a document whose vectors are
    /// from the old model has none that can be compared with a new question, whichever of its
    /// passages happen to be requested.
    /// </para>
    /// </summary>
    Task<int> MarkReindexRequiredAsync(
        EmbeddingSpace? currentSpace,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a document's record, its chunks, and its vectors from the index. The file itself
    /// on disk is not touched: removing something from a knowledge base is a statement about the
    /// index, not about the person's files.
    /// </summary>
    Task<bool> RemoveAsync(Guid documentId, CancellationToken cancellationToken = default);
}
