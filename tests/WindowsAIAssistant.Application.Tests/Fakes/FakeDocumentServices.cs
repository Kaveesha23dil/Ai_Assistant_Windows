using WindowsAIAssistant.Core.Abstractions.Documents;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models.Documents;

namespace WindowsAIAssistant.Application.Tests.Fakes;

/// <summary>
/// A stand-in reader that returns content a test has already built.
/// <para>
/// It exists so the analysis service can be tested for the decisions it makes — which passages
/// are sent, what happens when the provider refuses, what a cancellation does — without any
/// file on disk. A test that wanted to reach those decisions by way of a real extractor would
/// be testing the extractor instead.
/// </para>
/// </summary>
public sealed class FakeDocumentReader : IDocumentReader
{
    /// <summary>Gets or sets the content the next read returns.</summary>
    public DocumentContent Content { get; set; } = Empty();

    /// <summary>Gets or sets the failure the next read throws, instead of returning content.</summary>
    public DocumentException? ExceptionToThrow { get; set; }

    /// <summary>Gets the path the service asked for, so a test can check what was passed on.</summary>
    public string? LastPath { get; private set; }

    public int ReadCount { get; private set; }

    public Task<DocumentContent> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        LastPath = filePath;
        ReadCount++;
        cancellationToken.ThrowIfCancellationRequested();

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.FromResult(Content);
    }

    /// <summary>Builds content whose single section is the given text.</summary>
    public static DocumentContent WithText(
        string text,
        string fileName = "notes.txt",
        DocumentFileType fileType = DocumentFileType.PlainText,
        IEnumerable<DocumentWarning>? warnings = null)
    {
        var section = DocumentSection.Create(
            0,
            DocumentSectionKind.Heading,
            "Section 1",
            "section 1",
            text);

        var metadata = new DocumentMetadata
        {
            FileName = fileName,
            Extension = Path.GetExtension(fileName),
        };

        return DocumentContent.Create(
            fileName,
            fileType,
            metadata,
            [section],
            warnings);
    }

    /// <summary>Builds content from whole sections, one chunk-length's worth apart.</summary>
    public static DocumentContent WithSections(
        params string[] sections) =>
        DocumentContent.Create(
            "report.pdf",
            DocumentFileType.Pdf,
            new DocumentMetadata
            {
                FileName = "report.pdf",
                Extension = ".pdf",
            },
            sections.Select((text, index) => DocumentSection.Create(
                index,
                DocumentSectionKind.Page,
                $"Page {index + 1}",
                $"page {index + 1}",
                text)));

    private static DocumentContent Empty() =>
        WithText(string.Empty);
}

/// <summary>
/// A stand-in chunker that hands back a fixed set of chunks, so a test can control exactly
/// which passages the analysis service would have chosen.
/// </summary>
public sealed class FakeDocumentChunker : IDocumentChunker
{
    public List<DocumentChunk> Chunks { get; } = [];

    /// <summary>Gets the maximum the analysis service asked for.</summary>
    public int? LastMaximumChunks { get; private set; }

    public Task<IReadOnlyList<DocumentChunk>> ChunkAsync(
        DocumentContent content,
        int maximumChunks,
        CancellationToken cancellationToken = default)
    {
        LastMaximumChunks = maximumChunks;
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<DocumentChunk> limited = Chunks.Take(maximumChunks).ToList();
        return Task.FromResult(limited);
    }

    /// <summary>Adds a chunk of the given text at the given position in the document.</summary>
    public FakeDocumentChunker WithChunk(string text, int index = 0, string reference = "page 1")
    {
        Chunks.Add(new DocumentChunk
        {
            Id = Guid.NewGuid(),
            Sequence = Chunks.Count,
            Text = text,
            Sections = [reference],
            StartReference = reference,
            EndReference = reference,
        });

        return this;
    }
}
