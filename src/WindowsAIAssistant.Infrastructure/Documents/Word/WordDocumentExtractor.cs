using System.Text;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.Documents;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models.Documents;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.Documents.Word;

/// <summary>
/// Reads a word-processing document: its paragraphs in order, the headings among them, its
/// tables, and the properties it records about itself.
/// <para>
/// The document is read as XML rather than through the layout-aware reader. That is the right
/// trade for this purpose: what a summary or an answer needs is the text in the order a reader
/// would meet it and the names of the sections, and the layout API is built to answer where
/// things sit on a page, which is a different question and a much more expensive one.
/// </para>
/// <para>
/// The package is opened read-only and is never saved. Nothing here runs a macro, resolves an
/// external reference, or follows a link: a document is untrusted input, and the only thing
/// that is ever done with one is that its text is read.
/// </para>
/// </summary>
public sealed class WordDocumentExtractor : IDocumentExtractor
{
    private readonly IOptionsMonitor<DocumentOptions> _options;
    private readonly ILogger<WordDocumentExtractor> _logger;

    public WordDocumentExtractor(
        IOptionsMonitor<DocumentOptions> options,
        ILogger<WordDocumentExtractor> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public DocumentFileType FileType => DocumentFileType.Word;

    /// <inheritdoc />
    public bool CanExtract(string extension) =>
        extension.Equals(".docx", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task<DocumentContent> ExtractAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        // The file is not uploaded or trusted as anything but bytes on this machine, so this is
        // opened strictly for reading and closed without being written back.
        using var document = WordprocessingDocument.Open(filePath, isEditable: false);
        var main = document.MainDocumentPart
            ?? throw new DocumentException(
                "This document could not be read because it has no main content part.",
                ErrorCodes.DocumentExtractionFailed);

        var body = main.Document?.Body
            ?? throw new DocumentException(
                "This document could not be read because its content is missing.",
                ErrorCodes.DocumentExtractionFailed);

        var limit = _options.CurrentValue.MaximumExtractedCharacters;
        var sections = new List<DocumentSection>();
        var warnings = new List<DocumentWarning>();
        var characters = 0;
        var order = 0;
        var truncated = false;

        var currentHeading = "Beginning of document";
        var currentKind = DocumentSectionKind.Block;
        var current = new StringBuilder();
        var currentReference = string.Empty;

        void Flush()
        {
            var body2 = current.ToString().TrimEnd();
            if (body2.Length > 0 || sections.Count == 0)
            {
                sections.Add(DocumentSection.Create(
                    order++,
                    currentKind,
                    currentHeading,
                    currentReference,
                    body2));
            }

            current.Clear();
        }

        foreach (var element in body.ChildElements)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (element is DocumentFormat.OpenXml.Wordprocessing.Paragraph paragraph)
            {
                var text = ReadParagraphText(paragraph);
                if (text.Length == 0)
                {
                    continue;
                }

                if (characters + text.Length > limit)
                {
                    truncated = true;
                    break;
                }

                characters += text.Length;
                var style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;

                if (IsHeading(style))
                {
                    Flush();
                    currentHeading = text.Trim();
                    currentKind = DocumentSectionKind.Heading;
                    currentReference = $"the \"{currentHeading}\" heading";
                    current.AppendLine(text);
                    continue;
                }

                current.AppendLine(text);
                continue;
            }

            if (element is DocumentFormat.OpenXml.Wordprocessing.Table table)
            {
                var rendered = RenderTable(table);
                if (rendered.Length == 0)
                {
                    continue;
                }

                if (characters + rendered.Length > limit)
                {
                    truncated = true;
                    break;
                }

                // A table is kept as its own section: it is a structure a reader thinks in,
                // and burying it in the middle of a run of paragraphs is how a table's numbers
                // get read as prose.
                Flush();
                currentHeading = "Table";
                currentKind = DocumentSectionKind.Table;
                currentReference = $"table {order + 1}";
                current.AppendLine(rendered);
                Flush();
                currentHeading = "Beginning of document";
                currentKind = DocumentSectionKind.Block;
                currentReference = string.Empty;
            }
        }

        Flush();

        if (truncated)
        {
            warnings.Add(DocumentWarningMessages.Create(DocumentWarningKind.ContentTruncated));
        }

        if (ContainsImages(main))
        {
            warnings.Add(DocumentWarningMessages.Create(DocumentWarningKind.ImagesNotAnalyzed));
        }

        if (ContainsEmbeddedObjects(main))
        {
            warnings.Add(DocumentWarningMessages.Create(DocumentWarningKind.EmbeddedContentSkipped));
        }

        _logger.LogInformation(
            "Word document read. Sections extracted: {SectionCount}. Characters extracted: {CharacterCount}.",
            sections.Count,
            characters);

        var properties = ReadProperties(document);
        var fileName = Path.GetFileName(filePath);

        return Task.FromResult(DocumentContent.Create(
            fileName,
            DocumentFileType.Word,
            new DocumentMetadata
            {
                FileName = fileName,
                Extension = Path.GetExtension(filePath),
                Title = properties.Title,
                Author = properties.Author,
            },
            sections,
            warnings));
    }

    private static bool IsHeading(string? style) =>
        style is not null &&
        (style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) ||
         style.StartsWith("Title", StringComparison.OrdinalIgnoreCase) ||
         style.StartsWith("TOCHeading", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reads a paragraph's text, including the parts of it that live in separate XML elements
    /// such as hyperlinks and field results. A paragraph is joined with single spaces, because
    /// the element boundaries inside one carry no meaning for a reader.
    /// </summary>
    private static string ReadParagraphText(DocumentFormat.OpenXml.Wordprocessing.Paragraph paragraph)
    {
        var parts = new List<string>();
        var current = new StringBuilder();

        void Flush()
        {
            if (current.Length > 0)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
        }

        foreach (var node in paragraph.Descendants())
        {
            switch (node)
            {
                case DocumentFormat.OpenXml.Wordprocessing.Text text:
                    current.Append(text.Text);
                    break;

                // A tab or a line break inside a paragraph is whitespace to a reader, and
                // joining on it would otherwise split one sentence into two.
                case DocumentFormat.OpenXml.Wordprocessing.TabChar:
                case DocumentFormat.OpenXml.Wordprocessing.Break:
                    Flush();
                    parts.Add(" ");
                    break;
            }
        }

        Flush();

        return string.Join(string.Empty, parts).Trim();
    }

    /// <summary>
    /// Renders a table as pipe-separated rows, which is the form that survives being sent as
    /// text and read by a language model as a table rather than as prose. Word's merged cells
    /// and visual layout are not reproduced: a cell that spans several columns appears in the
    /// first and is empty in the rest, which is the honest reading of it.
    /// </summary>
    private static string RenderTable(DocumentFormat.OpenXml.Wordprocessing.Table table)
    {
        var rows = new List<string>();

        foreach (var row in table.Elements<DocumentFormat.OpenXml.Wordprocessing.TableRow>())
        {
            var cells = new List<string>();

            foreach (var cell in row.Elements<DocumentFormat.OpenXml.Wordprocessing.TableCell>())
            {
                var text = string.Join(
                    " ",
                    cell.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                        .Select(ReadParagraphText)
                        .Where(value => value.Length > 0));

                cells.Add(text.Replace("|", "/"));
            }

            if (cells.Count > 0)
            {
                rows.Add(string.Join(" | ", cells));
            }
        }

        return string.Join("\n", rows);
    }

    private static bool ContainsImages(MainDocumentPart main) =>
        main.ImageParts.Any() ||
        main.Document?.Descendants<DocumentFormat.OpenXml.Wordprocessing.Drawing>().Any() == true;

    private static bool ContainsEmbeddedObjects(MainDocumentPart main) =>
        main.EmbeddedObjectParts.Any() ||
        main.EmbeddedPackageParts.Any();

    private static (string? Title, string? Author) ReadProperties(WordprocessingDocument document)
    {
        try
        {
            // Reading the properties is a nicety. A document with a damaged or missing
            // properties part is still perfectly readable, so a failure here is not allowed to
            // fail the read.
            var properties = document.PackageProperties;
            return (Trim(properties.Title), Trim(properties.Creator));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            return (null, null);
        }

        static string? Trim(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
