using WindowsAIAssistant.Application.Documents.Queries.AskDocumentQuestion;
using WindowsAIAssistant.Application.Documents.Queries.SummarizeDocument;
using WindowsAIAssistant.Core.Abstractions.Documents;
using WindowsAIAssistant.Core.Models.Documents;

namespace WindowsAIAssistant.Application.Documents.Queries.StreamDocumentAnswer;

/// <summary>
/// Asks a document a question, or for a summary, and reports the answer as it arrives.
/// <para>
/// This exists so the interface layer streams a document answer the same way it streams anything
/// else: it names a document and a question, and receives updates. What it is not allowed to do
/// is read a file, rank passages, or decide how much text fits in a request, because every one
/// of those is a decision the analysis service makes once, and makes identically for this path
/// and for the two whole-answer ones.
/// </para>
/// </summary>
public sealed class StreamDocumentAnswerHandler
{
    private readonly IDocumentAnalysisService _documents;

    public StreamDocumentAnswerHandler(IDocumentAnalysisService documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    /// <summary>Streams the answer to a question about a document.</summary>
    public IAsyncEnumerable<DocumentAnalysisUpdate> AskAsync(
        AskDocumentQuestionQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        return _documents.StreamAnswerAsync(
            DocumentAnalysisRequest.AskQuestion(query.FilePath, query.Question),
            cancellationToken);
    }

    /// <summary>Streams the summary of a document.</summary>
    public IAsyncEnumerable<DocumentAnalysisUpdate> SummarizeAsync(
        SummarizeDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return _documents.StreamAnswerAsync(
            DocumentAnalysisRequest.Summarize(command.FilePath, command.Mode),
            cancellationToken);
    }
}
