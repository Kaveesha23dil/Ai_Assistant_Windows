using WindowsAIAssistant.Application.Documents.Handlers;
using WindowsAIAssistant.Application.DTOs;
using WindowsAIAssistant.Core.Abstractions.Documents;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Documents;

namespace WindowsAIAssistant.Application.Documents.Queries.SummarizeDocument;

/// <summary>
/// Asks for a summary of one document.
/// </summary>
public sealed record SummarizeDocumentCommand
{
    public SummarizeDocumentCommand(string filePath, DocumentSummaryMode mode = DocumentSummaryMode.Standard)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = filePath;
        Mode = mode;
    }

    public string FilePath { get; }

    public DocumentSummaryMode Mode { get; }

    public string FileName => Path.GetFileName(FilePath);
}

/// <summary>
/// Summarizes a document, and reports anything that got in the way of doing it properly.
/// </summary>
public sealed class SummarizeDocumentHandler
{
    private readonly IDocumentAnalysisService _documents;

    public SummarizeDocumentHandler(IDocumentAnalysisService documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    public async Task<DocumentAnalysisDto> HandleAsync(
        SummarizeDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await _documents
            .SummarizeAsync(DocumentAnalysisRequest.Summarize(command.FilePath, command.Mode), cancellationToken)
            .ConfigureAwait(false);

        return DocumentAnalysisMapper.ToDto(result);
    }
}
