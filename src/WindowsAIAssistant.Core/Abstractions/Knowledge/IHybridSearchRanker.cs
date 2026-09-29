using WindowsAIAssistant.Core.Models.Knowledge;

namespace WindowsAIAssistant.Core.Abstractions.Knowledge;

/// <summary>
/// Combines the two ways a passage can be found into one ranking.
/// <para>
/// A vector search finds "authentication" when somebody asks about "log-in", which no amount of
/// matching words will do. A word search finds <c>JWT_REFRESH_TOKEN</c> when a model has
/// averaged it into a vector, and puts the passage that literally contains the identifier ahead
/// of one that is merely about it. Neither is sufficient, so the two are combined here, with
/// weights from configuration, and the weights are the only tunable part.
/// </para>
/// <para>
/// Nothing more elaborate is claimed. A learned reranker is a later step and would sit at this
/// seam; what is here is the honest minimum that fixes the two failure modes above.
/// </para>
/// </summary>
public interface IHybridSearchRanker
{
    /// <summary>
    /// Scores and orders passages against a question.
    /// </summary>
    /// <param name="passages">
    /// The candidate passages. Those with a vector score are treated as semantic finds; those
    /// without are keyword finds, and are ranked on wording alone rather than being given a
    /// fabricated vector score of zero.
    /// </param>
    /// <param name="question">The question being answered.</param>
    /// <param name="limit">The most passages to return, before the threshold is applied.</param>
    IReadOnlyList<KnowledgeSearchResult> Rank(
        IReadOnlyList<KnowledgeSearchResult> passages,
        string question,
        int limit);
}
