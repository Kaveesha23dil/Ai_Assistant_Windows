using WindowsAIAssistant.Core.Models.Embeddings;
using WindowsAIAssistant.Core.Models.Knowledge;

namespace WindowsAIAssistant.Core.Abstractions.Knowledge;

/// <summary>
/// Finds the passages worth answering a question from, and nothing else.
/// <para>
/// Retrieval is the whole of what this does. It does not build a prompt, choose a provider,
/// consent to anything, or answer. Keeping that line sharp is what makes the difference between
/// "the search found nothing" and "the model had nothing to work with" two separate, separately
/// reportable facts, and it is why a question about a document that says nothing about it can be
/// answered by saying so rather than by being answered.
/// </para>
/// </summary>
public interface IRagRetriever
{
    /// <summary>
    /// Gets the embedding space the last search ran in, or <see langword="null"/> before the
    /// first one. Read by the interface so it can tell a person that their index needs
    /// reindexing before they conclude the assistant has forgotten their documents.
    /// </summary>
    EmbeddingSpace? CurrentSpace { get; }

    /// <summary>
    /// Finds passages for a question, ranked, with duplicates and near-neighbours thinned out.
    /// </summary>
    /// <param name="query">The question, its base, and any filters.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    Task<IReadOnlyList<KnowledgeSearchResult>> RetrieveAsync(
        KnowledgeQuery query,
        CancellationToken cancellationToken = default);
}
