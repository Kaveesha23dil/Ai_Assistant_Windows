using WindowsAIAssistant.Core.Models.Documents;

namespace WindowsAIAssistant.Core.Abstractions.Documents;

/// <summary>
/// Orders the parts of a document by how likely they are to hold the answer to a question.
/// <para>
/// This is lexical scoring, not understanding: it looks for the question's words in the text.
/// That is enough to find "the project deadline is October 15" when asked when the deadline is,
/// and it is honest about its limits, because a chunk it scores zero is genuinely one that
/// shares no vocabulary with the question. Nothing here claims to know what the words mean,
/// and nothing here silently drops a low-scoring chunk that did match.
/// </para>
/// </summary>
public interface IDocumentChunkRanker
{
    /// <summary>
    /// Returns the chunks that best match a question, best first, at most
    /// <paramref name="maximumResults"/> of them. When nothing matches at all, the result is
    /// empty rather than filled with the leading chunks, so the caller can say the question was
    /// not answered from this document instead of summarizing the wrong part of it.
    /// </summary>
    IReadOnlyList<RankedDocumentChunk> Rank(
        IReadOnlyList<DocumentChunk> chunks,
        string query,
        int maximumResults);
}
