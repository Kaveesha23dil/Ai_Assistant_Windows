using WindowsAIAssistant.Application.Documents.Handlers;
using WindowsAIAssistant.Application.DTOs;
using WindowsAIAssistant.Core.Abstractions.Documents;
using WindowsAIAssistant.Core.Models.Documents;

namespace WindowsAIAssistant.Application.Documents.Queries.AskDocumentQuestion;

/// <summary>
/// Asks one question about one document.
/// </summary>
public sealed record AskDocumentQuestionQuery
{
    public AskDocumentQuestionQuery(string filePath, string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        FilePath = filePath;
        Question = question.Trim();
    }

    public string FilePath { get; }

    public string Question { get; }

    public string FileName => Path.GetFileName(FilePath);
}

/// <summary>
/// Asks a question about a document and returns an answer taken from the document, or says
/// that the document does not answer it.
/// </summary>
public sealed class AskDocumentQuestionHandler
{
    private readonly IDocumentAnalysisService _documents;

    public AskDocumentQuestionHandler(IDocumentAnalysisService documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    public async Task<DocumentAnalysisDto> HandleAsync(
        AskDocumentQuestionQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await _documents
            .AskQuestionAsync(DocumentAnalysisRequest.AskQuestion(query.FilePath, query.Question), cancellationToken)
            .ConfigureAwait(false);

        return DocumentAnalysisMapper.ToDto(result);
    }
}
