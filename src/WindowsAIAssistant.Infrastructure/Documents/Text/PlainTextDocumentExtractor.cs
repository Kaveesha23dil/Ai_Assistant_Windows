using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.Documents;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models.Documents;
using WindowsAIAssistant.Infrastructure.Configuration.Options;
using WindowsAIAssistant.Infrastructure.Documents;

namespace WindowsAIAssistant.Infrastructure.Documents.Text;

/// <summary>
/// Reads text, and anything that is text wearing a different extension.
/// <para>
/// The reading is the same for all of them, so they are one extractor rather than several:
/// adding a supported text extension is a line in the type detector instead of a class. The
/// file is read a line at a time and the text is built into one buffer with a ceiling, so the
/// memory used tracks the limit rather than the file.
/// </para>
/// <para>
/// Markdown is given one small courtesy, because headings are the only structure a plain text
/// format reliably has: its sections are its headings, so a summary or an answer can say which
/// heading something came from. Everything else is one block, and the chunker divides it.
/// </para>
/// </summary>
public sealed class PlainTextDocumentExtractor : IDocumentExtractor
{
    private readonly IOptionsMonitor<DocumentOptions> _options;
    private readonly ILogger<PlainTextDocumentExtractor> _logger;

    public PlainTextDocumentExtractor(
        IOptionsMonitor<DocumentOptions> options,
        ILogger<PlainTextDocumentExtractor> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public DocumentFileType FileType => DocumentFileType.PlainText;

    /// <inheritdoc />
    public bool CanExtract(string extension) => DocumentTypeDetector.IsPlainTextExtension(extension);

    /// <inheritdoc />
    public async Task<DocumentContent> ExtractAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var options = _options.CurrentValue;
        var warnings = new List<DocumentWarning>();
        var limit = options.MaximumExtractedCharacters;

        var (text, truncated) = await ReadAsync(filePath, limit, cancellationToken).ConfigureAwait(false);

        if (truncated)
        {
            warnings.Add(DocumentWarningMessages.Create(DocumentWarningKind.ContentTruncated));
        }

        // A file whose bytes were not valid text is read on a best-effort basis rather than
        // refused: refusing would mean a single mis-encoded byte makes a document unreadable,
        // and the person would have no way to see any of it.
        if (text.DecodingWasLossy)
        {
            warnings.Add(DocumentWarningMessages.Create(DocumentWarningKind.UnknownEncoding));
        }

        var fileName = Path.GetFileName(filePath);
        var isMarkdown = Path.GetExtension(filePath).Equals(".md", StringComparison.OrdinalIgnoreCase);

        var sections = isMarkdown
            ? SplitMarkdown(text.Value)
            : [DocumentSection.Create(0, DocumentSectionKind.Block, fileName, $"the whole of {fileName}", text.Value)];

        _logger.LogInformation(
            "Text file read. Sections: {SectionCount}. Characters extracted: {CharacterCount}. Encoding loss: {LossyEncoding}.",
            sections.Count,
            text.Value.Length,
            text.DecodingWasLossy);

        return DocumentContent.Create(
            fileName,
            DocumentFileType.PlainText,
            new DocumentMetadata { FileName = fileName, Extension = Path.GetExtension(filePath) },
            sections,
            warnings);
    }

    /// <summary>
    /// Reads the file a line at a time, stopping once the limit is reached.
    /// <para>
    /// The bytes are decoded strictly first. A file that is not valid text is re-read with
    /// replacement characters and flagged, rather than being reported as a document that
    /// happened to contain invalid characters somewhere in the middle: the first is a
    /// statement about the file, the second is a statement about the answer.
    /// </para>
    /// </summary>
    private static async Task<(DecodedText Text, bool Truncated)> ReadAsync(
        string filePath,
        int limit,
        CancellationToken cancellationToken)
    {
        var strict = await ReadWithAsync(filePath, limit, lossy: false, cancellationToken).ConfigureAwait(false);
        if (!strict.Lossy)
        {
            return (new DecodedText(strict.Value, DecodingWasLossy: false), strict.Truncated);
        }

        var lenient = await ReadWithAsync(filePath, limit, lossy: true, cancellationToken).ConfigureAwait(false);
        return (new DecodedText(lenient.Value, DecodingWasLossy: true), lenient.Truncated);
    }

    private static async Task<(string Value, bool Truncated, bool Lossy)> ReadWithAsync(
        string filePath,
        int limit,
        bool lossy,
        CancellationToken cancellationToken)
    {
        var encoding = lossy
            ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false)
            : new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        try
        {
            return await ReadStreamAsync(filePath, limit, encoding, cancellationToken).ConfigureAwait(false);
        }
        catch (DecoderFallbackException) when (!lossy)
        {
            return (string.Empty, false, Lossy: true);
        }
    }

    private static async Task<(string Value, bool Truncated, bool Lossy)> ReadStreamAsync(
        string filePath,
        int limit,
        Encoding encoding,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder(Math.Min(limit, 64 * 1024));
        var truncated = false;

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        // Byte-order marks are followed so the reader works out UTF-8 from UTF-16 for itself.
        // A file with no mark is read as UTF-8, which is what a text file without a mark is
        // overwhelmingly likely to be, and the strict pass above is what catches the exception.
        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);

        while (builder.Length < limit)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(line);

            if (builder.Length >= limit)
            {
                truncated = true;
                break;
            }
        }

        return (builder.ToString(0, Math.Min(builder.Length, limit)), truncated, Lossy: false);
    }

    /// <summary>
    /// Splits Markdown into one section per heading, keeping the heading itself. A document with
    /// no headings is left as a single block rather than invented into pieces.
    /// </summary>
    private static IReadOnlyList<DocumentSection> SplitMarkdown(string text)
    {
        var lines = text.Split('\n');
        var sections = new List<DocumentSection>();
        var order = 0;
        var currentName = "Beginning of document";
        var current = new StringBuilder();

        void Flush()
        {
            var body = current.ToString().Trim();
            if (body.Length == 0 && order > 0)
            {
                return;
            }

            sections.Add(DocumentSection.Create(
                order++,
                order == 1 ? DocumentSectionKind.Block : DocumentSectionKind.Heading,
                currentName,
                $"the \"{currentName}\" section",
                body));
            current.Clear();
        }

        foreach (var line in lines)
        {
            if (line.StartsWith('#'))
            {
                Flush();
                currentName = line.TrimStart('#', ' ').Trim();
                if (currentName.Length == 0)
                {
                    currentName = "Untitled section";
                }

                current.AppendLine(line);
                continue;
            }

            current.AppendLine(line);
        }

        Flush();

        return sections.Count == 0
            ? [DocumentSection.Create(0, DocumentSectionKind.Block, "Document", "the document", text)]
            : sections;
    }

    private readonly record struct DecodedText(string Value, bool DecodingWasLossy);
}
