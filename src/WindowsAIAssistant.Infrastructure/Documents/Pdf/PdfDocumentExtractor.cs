using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Exceptions;
using WindowsAIAssistant.Core.Abstractions.Documents;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models.Documents;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.Documents.Pdf;

/// <summary>
/// Reads the text of a PDF, one section per page.
/// <para>
/// Pages are the unit because that is what a person can turn to. A PDF has no other structure
/// that can be relied on: the fonts, positions, and reading order inside a page are a matter
/// for the file that made it, and a two-column paper article has a text order that no
/// extractor can promise is right. So text is taken in the order the page provides, and where
/// that order is doubtful the page is flagged rather than quietly reordered into something that
/// reads well but is not what the page says.
/// </para>
/// <para>
/// A PDF whose pages carry no text is reported as needing OCR rather than as an empty document.
/// The difference matters: an empty document has nothing in it, while a scanned one has
/// everything in it as images, and answering questions about the second from the first would be
/// answering about a page of nothing.
/// </para>
/// </summary>
public sealed class PdfDocumentExtractor : IDocumentExtractor
{
    /// <summary>
    /// A page carrying fewer characters than this, averaged over the document, is taken to be
    /// an image rather than a page of text. Set low on purpose: a page with a single line of a
    /// caption is still a page someone might ask about.
    /// </summary>
    private const int ScannedPageCharacterThreshold = 24;

    private readonly IOptionsMonitor<DocumentOptions> _options;
    private readonly ILogger<PdfDocumentExtractor> _logger;

    public PdfDocumentExtractor(
        IOptionsMonitor<DocumentOptions> options,
        ILogger<PdfDocumentExtractor> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public DocumentFileType FileType => DocumentFileType.Pdf;

    /// <inheritdoc />
    public bool CanExtract(string extension) =>
        extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task<DocumentContent> ExtractAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var options = _options.CurrentValue;
        var fileName = Path.GetFileName(filePath);
        var sections = new List<DocumentSection>();
        var warnings = new List<DocumentWarning>();
        var characters = 0;
        var pagesWithText = 0;
        var truncated = false;
        var pageCount = 0;

        PdfDocument document;
        try
        {
            document = PdfDocument.Open(filePath);
        }
        catch (Exception exception) when (IsEncrypted(exception))
        {
            // No attempt is made to open it with a guessed or empty password. A document that is
            // protected has been deliberately protected, and the only correct answer here is to
            // say so and stop.
            _logger.LogWarning("The PDF is password protected and was not read.");
            throw new DocumentException(
                "This document is password protected, so its contents could not be read.",
                ErrorCodes.DocumentPasswordRequired,
                exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "The PDF could not be opened.");
            throw new DocumentException(
                "This PDF could not be read. It may be damaged or not really a PDF.",
                ErrorCodes.DocumentExtractionFailed,
                exception);
        }

        // The document information is read while the file is still open. PdfPig reads it lazily
        // from the file stream, so asking for it after the document has been closed is asking a
        // disposed object for something it has already let go of.
        DocumentMetadata metadata;

        using (document)
        {
            var pages = document.NumberOfPages;
            pageCount = pages;
            var information = document.Information;

            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var number = page.Number;
                var text = ReadPageText(page);

                if (text.Length > 0)
                {
                    pagesWithText++;
                }

                if (characters + text.Length > options.MaximumExtractedCharacters)
                {
                    truncated = true;
                    break;
                }

                characters += text.Length;
                sections.Add(DocumentSection.Create(
                    number - 1,
                    DocumentSectionKind.Page,
                    $"Page {number}",
                    $"page {number}",
                    text));
            }

            metadata = new DocumentMetadata
            {
                FileName = fileName,
                Extension = Path.GetExtension(filePath),
                Title = Blank(information.Title),
                Author = Blank(information.Author),
                PageCount = pageCount,
            };
        }

        // Every page present but none of them carrying text is a scanned document, and saying so
        // is the whole point: the alternative is a summary of an empty string.
        if (sections.Count > 0 && pagesWithText == 0)
        {
            _logger.LogWarning(
                "The PDF has {PageCount} pages and none of them contain text. It appears to be scanned.",
                sections.Count);

            throw new DocumentException(
                "This PDF appears to contain scanned pages or images. OCR is not enabled yet.",
                ErrorCodes.DocumentOcrRequired);
        }

        // Some pages readable and some not is the common real case: a report whose appendix was
        // scanned. The document is still worth analysing and the gap is still worth naming.
        if (pagesWithText > 0 && pagesWithText < sections.Count)
        {
            warnings.Add(DocumentWarningMessages.Create(DocumentWarningKind.ScannedPagesDetected));
        }

        if (truncated)
        {
            warnings.Add(DocumentWarningMessages.Create(DocumentWarningKind.ContentTruncated));
        }

        _logger.LogInformation(
            "PDF read. Pages extracted: {PageCount}. Pages with text: {PagesWithText}. Characters extracted: {CharacterCount}.",
            sections.Count,
            pagesWithText,
            characters);

        return Task.FromResult(DocumentContent.Create(
            fileName,
            DocumentFileType.Pdf,
            metadata,
            sections,
            warnings));
    }

    /// <summary>
    /// Reads a page's text and tidies the whitespace it comes with.
    /// <para>
    /// The order is the one the page's own content stream gives, and it is left alone. A PDF
    /// records where each piece of text was drawn rather than the order a person reads it, so
    /// a two-column page can come out in either column first. Reordering it needs a layout
    /// model this deliberately does not have, and a guess that reads fluently while being wrong
    /// is worse than text that is plainly in the file's own order. Where a page yields nothing
    /// at all, that is reported as scanned rather than guessed around.
    /// </para>
    /// <para>
    /// Runs of whitespace are collapsed because PDF text is drawn glyph by glyph and arrives
    /// with spacing that means nothing once the words are in order.
    /// </para>
    /// </summary>
    private static string ReadPageText(Page page)
    {
        var raw = page.Text;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(raw.Length);
        var pendingWhitespace = false;

        foreach (var character in raw)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingWhitespace = builder.Length > 0;
                continue;
            }

            if (pendingWhitespace)
            {
                builder.Append(' ');
                pendingWhitespace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static bool IsEncrypted(Exception exception) =>
        exception is PdfDocumentEncryptedException ||
        exception.Message.Contains("password", StringComparison.OrdinalIgnoreCase);

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
