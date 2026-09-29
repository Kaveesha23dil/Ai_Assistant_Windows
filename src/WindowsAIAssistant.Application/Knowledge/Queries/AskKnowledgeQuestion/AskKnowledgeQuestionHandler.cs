using System.Runtime.CompilerServices;
using WindowsAIAssistant.Core.Abstractions.Knowledge;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Knowledge;

namespace WindowsAIAssistant.Application.Knowledge.Queries.AskKnowledgeQuestion;

/// <summary>
/// Asks a question of the index, from the knowledge page or from the chat page's knowledge mode.
/// <para>
/// A thin wrapper over <see cref="IRagService"/>, and deliberately so. Everything that decides
/// what a question retrieves — which base, which filters, how many passages, whether word
/// matching alone is acceptable — is settled in the query record and the retriever below, and
/// re-deciding any of it here would give the voice path and the page different answers to the
/// same sentence.
/// </para>
/// </summary>
public sealed class AskKnowledgeQuestionHandler
{
    private readonly IRagService _rag;
    private readonly IKnowledgeBaseRepository _bases;
    private readonly KnowledgeProcessingLimits _limits;

    public AskKnowledgeQuestionHandler(
        IRagService rag,
        IKnowledgeBaseRepository bases,
        KnowledgeProcessingLimits limits)
    {
        ArgumentNullException.ThrowIfNull(rag);
        ArgumentNullException.ThrowIfNull(bases);
        ArgumentNullException.ThrowIfNull(limits);

        _rag = rag;
        _bases = bases;
        _limits = limits;
    }

    /// <summary>
    /// Answers a question in full.
    /// <para>
    /// An empty question is refused here rather than passed down. A retrieval with no terms in it
    /// has no way to be a meaningful search, and letting it run would build an answer out of
    /// whatever the index happened to rank first.
    /// </para>
    /// </summary>
    public async Task<KnowledgeAnswer> HandleAsync(
        string question,
        Guid? knowledgeBaseId = null,
        int? resultCount = null,
        CancellationToken cancellationToken = default)
    {
        var prepared = await BuildAsync(question, knowledgeBaseId, resultCount, cancellationToken)
            .ConfigureAwait(false);

        return prepared.Refusal is not null
            ? prepared.Refusal
            : KnowledgeAnswer.From(await _rag.AskAsync(prepared.Query!, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Answers a question as it is written, and reports the sources before the first word of the
    /// answer, because that is the order the service produces them in.
    /// </summary>
    public async IAsyncEnumerable<RagStreamUpdate> HandleStreamingAsync(
        string question,
        Guid? knowledgeBaseId = null,
        int? resultCount = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var prepared = await BuildAsync(question, knowledgeBaseId, resultCount, cancellationToken)
            .ConfigureAwait(false);

        if (prepared.Refusal is { } refused)
        {
            yield return RagStreamUpdate.Failed(
                refused.ErrorCode ?? ErrorCodes.ValidationError,
                refused.ErrorMessage ?? "That question could not be asked.");

            yield break;
        }

        await foreach (var update in _rag.AskStreamingAsync(prepared.Query!, cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }

    /// <summary>
    /// Finds the passages a question would be answered from, without writing an answer.
    /// <para>
    /// For showing somebody what is in their index, and for the tests that need to assert on
    /// ranking rather than on a sentence produced by a provider.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(
        string question,
        Guid? knowledgeBaseId = null,
        int? resultCount = null,
        CancellationToken cancellationToken = default)
    {
        var prepared = await BuildAsync(question, knowledgeBaseId, resultCount, cancellationToken)
            .ConfigureAwait(false);

        return prepared.Query is null
            ? []
            : await _rag.SearchAsync(prepared.Query, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves a question into something the retriever can be asked.
    /// <para>
    /// A refusal rather than an exception, because a person who types nothing has made a
    /// mistake, not crashed the application, and the page has to be able to say what to do about
    /// it instead of showing a failed request.
    /// </para>
    /// </summary>
    private async Task<PreparedQuery> BuildAsync(
        string question,
        Guid? knowledgeBaseId,
        int? resultCount,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return PreparedQuery.Refused(
                ErrorCodes.ValidationError,
                "Type a question to ask your documents.");
        }

        // The base is resolved here rather than left to a default parameter, because a question
        // asked with no base named is a question about the base in use, not about a base called
        // "default" that may have been deleted.
        var knowledgeBase = knowledgeBaseId is { } wanted
            ? await _bases.GetAsync(wanted, cancellationToken).ConfigureAwait(false)
            : await _bases.GetOrCreateDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (knowledgeBase is null)
        {
            return PreparedQuery.Refused(
                ErrorCodes.KnowledgeBaseNotFound,
                "That knowledge base no longer exists.");
        }

        var count = Math.Clamp(
            resultCount ?? _limits.DefaultRetrievalCount,
            1,
            _limits.MaximumRetrievalCount);

        return new PreparedQuery(KnowledgeQuery.Create(knowledgeBase.Id, question, count), null);
    }

    private sealed record PreparedQuery(KnowledgeQuery? Query, KnowledgeAnswer? Refusal)
    {
        public static PreparedQuery Refused(string errorCode, string message) =>
            new(
                null,
                new KnowledgeAnswer
                {
                    IsSuccess = false,
                    Answer = string.Empty,
                    Sources = [],
                    ErrorCode = errorCode,
                    ErrorMessage = message,
                });
    }
}
