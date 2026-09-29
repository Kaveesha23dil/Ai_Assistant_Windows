using WindowsAIAssistant.Core.Models.Knowledge;

namespace WindowsAIAssistant.Core.Abstractions.Knowledge;

/// <summary>
/// Answers a question from a knowledge base.
/// <para>
/// The single entry point the chat page, the knowledge page, and the voice path all use. They
/// differ in how the question arrives and in how the answer is shown, and none of them differs
/// in what happens to a person's documents on the way — which is the whole reason there is one
/// interface rather than three call paths into retrieval and a provider.
/// </para>
/// <para>
/// It lives in Core and not in Application for the same reason <c>IAIService</c> does: the
/// voice layer and the interface both need to name it, and neither may reach downward into
/// retrieval, a store, or a provider to get it.
/// </para>
/// </summary>
public interface IRagService
{
    /// <summary>
    /// Answers a question and returns the whole answer at once.
    /// </summary>
    /// <param name="query">The question, its base, and any filters.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    Task<RagAnswer> AskAsync(KnowledgeQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers a question a piece at a time.
    /// <para>
    /// Separate rather than an option on the first method because the retrieval and the context
    /// have to be finished before the first word of the answer exists: a stream cannot report
    /// which documents it drew on until the search is already over. The sources therefore arrive
    /// as a completed update before any text, so a caller can show them while the answer is still
    /// being written.
    /// </para>
    /// </summary>
    IAsyncEnumerable<RagStreamUpdate> AskStreamingAsync(
        KnowledgeQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds passages for a question and returns them without answering anything.
    /// <para>
    /// For showing somebody what is in their index, and for the tests that need to assert on
    /// ranking. It is here rather than on <c>IRagRetriever</c> alone because the interface and the
    /// voice layer should not have to know which of the two is the narrower one.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(
        KnowledgeQuery query,
        CancellationToken cancellationToken = default);
}
