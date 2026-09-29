using WindowsAIAssistant.Core.Models.Knowledge;

namespace WindowsAIAssistant.Core.Abstractions.Knowledge;

/// <summary>
/// Stores and reads knowledge bases.
/// <para>
/// Declared in Core and implemented in Infrastructure so that the Application layer can create,
/// rename, and delete a base without a database reference anywhere in it. That is what keeps
/// this feature replaceable: pointing it at a different store means another implementation of
/// this interface and no change above it.
/// </para>
/// </summary>
public interface IKnowledgeBaseRepository
{
    /// <summary>
    /// Returns the base used when a question does not name one, creating the default base the
    /// first time there is none.
    /// <para>
    /// Creating it on demand rather than during initialization means a first run does not need
    /// setup before anything works, and it means a base deleted on purpose stays deleted until
    /// somebody actually asks a question.
    /// </para>
    /// </summary>
    Task<KnowledgeBase> GetOrCreateDefaultAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists every base, oldest first.</summary>
    Task<IReadOnlyList<KnowledgeBase>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Finds one base.</summary>
    Task<KnowledgeBase?> GetAsync(Guid knowledgeBaseId, CancellationToken cancellationToken = default);

    /// <summary>Adds a base.</summary>
    Task<KnowledgeBase> AddAsync(KnowledgeBase knowledgeBase, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a base's name and description.
    /// <para>
    /// The counts and the timestamps are not taken from the caller: they are facts about the
    /// documents, and a row that could report a document count nobody counted would eventually
    /// report one that is wrong.
    /// </para>
    /// </summary>
    Task RenameAsync(
        Guid knowledgeBaseId,
        string name,
        string? description,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a base and everything indexed in it, leaving the original files exactly where
    /// they were.
    /// </summary>
    Task<bool> DeleteAsync(Guid knowledgeBaseId, CancellationToken cancellationToken = default);

    /// <summary>Recomputes and stores a base's document and chunk counts.</summary>
    Task RefreshCountsAsync(Guid knowledgeBaseId, CancellationToken cancellationToken = default);
}
