using WindowsAIAssistant.Core.Models.Documents;

namespace WindowsAIAssistant.Core.Abstractions.Documents;

/// <summary>
/// Answers something about a document: summarizes it, or answers a question about it.
/// <para>
/// This is the seam that keeps document handling out of the rest of the application. The chat
/// page, the voice path, and any future automation all call this, and none of them learn how a
/// PDF is read, how a long document is made to fit in a request, or what a grounded answer has
/// to contain.
/// </para>
/// </summary>
public interface IDocumentAnalysisService
{
    /// <summary>
    /// Reads a document and summarizes it. A document short enough to fit in one request is
    /// summarized in one pass; a longer one is summarized a part at a time and the parts are
    /// then summarized together.
    /// </summary>
    /// <returns>
    /// The summary, with the places it came from and any limitations met while reading. A
    /// failure is returned in the same shape, carrying a code and a sentence fit to show.
    /// </returns>
    Task<DocumentAnalysisResult> SummarizeAsync(
        DocumentAnalysisRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a document and answers a question about it, using only what the document says.
    /// </summary>
    /// <returns>
    /// The answer, the places in the document it came from, and any limitations met while
    /// reading. A question the document does not answer is reported as unanswered, never filled
    /// in from elsewhere.
    /// </returns>
    Task<DocumentAnalysisResult> AskQuestionAsync(
        DocumentAnalysisRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers a question about a document, or summarizes it, and reports the answer as it
    /// arrives rather than after it has finished.
    /// <para>
    /// This is the same work as the two methods above, over the same AI service, and is what the
    /// document panel uses. The difference is only in when the text is handed over: a person
    /// asking about a long document otherwise watches nothing at all for the length of an
    /// answer, with no way to tell that from a request that has stalled.
    /// </para>
    /// <para>
    /// The work is not duplicated for streaming. The reading, ranking, and per-part summarizing
    /// are the same code, and only the final request — the one whose text is actually shown — is
    /// streamed, because showing the parts of a long document's summary as they are produced
    /// would show text that is about to be replaced by a summary of all of it.
    /// </para>
    /// </summary>
    /// <returns>
    /// A sequence that ends with exactly one <see cref="DocumentAnalysisUpdateKind.Completed"/>
    /// or <see cref="DocumentAnalysisUpdateKind.Failed"/> update, so a caller can always tell a
    /// finished answer from a partial one. A failure arrives as the last update rather than as
    /// an exception, matching the other two methods; cancellation is the exception, and is not
    /// reported as a failure.
    /// </returns>
    IAsyncEnumerable<DocumentAnalysisUpdate> StreamAnswerAsync(
        DocumentAnalysisRequest request,
        CancellationToken cancellationToken = default);
}