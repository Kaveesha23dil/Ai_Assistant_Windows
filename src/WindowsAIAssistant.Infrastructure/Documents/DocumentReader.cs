using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.Documents;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models.Documents;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.Documents;

/// <summary>
/// Validates a file, picks the reader that can open it, and hands back what was found.
/// <para>
/// All four checks happen here, in one place, and each of them fails in a way that names its
/// own problem. That is the reason this type exists at all: left to callers, the size check is
/// the one that gets skipped, and a file that was never size-checked is a file that can bring
/// the application down before anything has a chance to notice.
/// </para>
/// <para>
/// Nothing here keeps the path. The reader passes the name to the extractor and returns a
/// document that carries only the name, so the full path does not travel any further than it
/// has to and cannot end up in a summary or a log line by being carried along.
/// </para>
/// </summary>
public sealed class DocumentReader : IDocumentReader
{
    private readonly IDocumentTypeDetector _detector;
    private readonly IDocumentExtractorFactory _factory;
    private readonly IOptionsMonitor<DocumentOptions> _options;
    private readonly ILogger<DocumentReader> _logger;

    public DocumentReader(
        IDocumentTypeDetector detector,
        IDocumentExtractorFactory factory,
        IOptionsMonitor<DocumentOptions> options,
        ILogger<DocumentReader> logger)
    {
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _detector = detector;
        _factory = factory;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DocumentContent> ReadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var options = _options.CurrentValue;
        if (!options.Enabled)
        {
            throw new DocumentException(
                "Reading documents is turned off in this application's settings.",
                ErrorCodes.DocumentFormatUnsupported);
        }

        var fileName = Path.GetFileName(filePath);

        if (!File.Exists(filePath))
        {
            throw new DocumentException(
                $"The file \"{fileName}\" could not be found.",
                ErrorCodes.DocumentNotFound);
        }

        var fileType = ValidateType(filePath, fileName);

        // Checked before anything is opened, and by length rather than by reading: the point is
        // to refuse a file without ever loading it.
        var length = new FileInfo(filePath).Length;
        var maximum = (long)options.MaximumFileSizeMb * 1024 * 1024;
        if (length > maximum)
        {
            _logger.LogWarning(
                "A document was refused because it is larger than the configured limit. Size in megabytes: {SizeMb}.",
                length / (1024 * 1024));
            throw new DocumentException(
                $"This file is larger than the {options.MaximumFileSizeMb} megabyte limit, so it was not opened.",
                ErrorCodes.DocumentTooLarge);
        }

        _logger.LogInformation(
            "Document extraction started. Format: {Format}. Size in megabytes: {SizeMb}.",
            fileType,
            Math.Round(length / (1024d * 1024d), 2));

        var extractor = _factory.GetExtractor(fileType);
        DocumentContent content;

        try
        {
            content = await extractor.ExtractAsync(filePath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Document extraction cancelled by caller.");
            throw;
        }
        catch (DocumentException)
        {
            // Already carries a code and a sentence fit to show. Rewrapping it here would only
            // replace a specific reason with a general one.
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning("A document could not be opened because access was denied.");
            throw new DocumentException(
                $"This file could not be opened: {fileName}. Access was denied.",
                ErrorCodes.DocumentAccessDenied,
                exception);
        }
        catch (Exception exception)
        {
            // The exception is logged and kept for diagnostics, and nothing from it is copied
            // into the message: a parser's own text can contain the file's path and fragments
            // of its contents.
            _logger.LogError(exception, "A document could not be read.");
            throw new DocumentException(
                $"This file could not be read: {fileName}.",
                ErrorCodes.DocumentExtractionFailed,
                exception);
        }

        if (content.IsEmpty)
        {
            _logger.LogWarning("A document was read but contained no text.");

            // An empty document is a real answer, not a failure to read: a spreadsheet of blank
            // cells is readable and contains nothing, and saying so is more useful to a person
            // than reporting that reading it went wrong.
            throw new DocumentException(
                $"No readable text was found in {fileName}.",
                ErrorCodes.DocumentEmpty);
        }

        _logger.LogInformation(
            "Document extraction completed. Format: {Format}. Sections: {SectionCount}. Characters: {CharacterCount}.",
            content.FileType,
            content.Sections.Count,
            content.CharacterCount);

        return content;
    }

    private DocumentFileType ValidateType(string filePath, string fileName)
    {
        // The name is checked against what the file actually is before the extension is trusted.
        // A PDF renamed to .txt would otherwise be read as a page of binary punctuation and
        // reported as a text file, which is a worse answer than being told the extension lies.
        var declared = _detector.Detect(filePath);
        var actual = _detector is DocumentTypeDetector concrete
            ? concrete.DetectFromSignature(filePath)
            : DocumentFileType.Unknown;

        if (declared is DocumentFileType.Unknown)
        {
            // Checked before the general refusal because there is something the person can do
            // about this one, and saying so is the difference between a dead end and an
            // instruction.
            if (DocumentTypeDetector.IsLegacyOfficeExtension(Path.GetExtension(filePath)))
            {
                throw new DocumentException(
                    $"\"{fileName}\" is an older Office file that this application cannot read. Save it in the current format (.docx, .xlsx, or .pptx) and try again.",
                    ErrorCodes.DocumentFormatUnsupported);
            }

            throw new DocumentException(
                $"\"{fileName}\" is not a file type this application can read.",
                ErrorCodes.DocumentFormatUnsupported);
        }

        if (actual is not DocumentFileType.Unknown && actual != declared && declared != DocumentFileType.PlainText)
        {
            throw new DocumentException(
                $"\"{fileName}\" does not match its file extension, so it was not opened.",
                ErrorCodes.DocumentFormatUnsupported);
        }

        // A legacy binary Office file carries a supported-looking extension, and opening it with
        // the modern reader produces a package error rather than a document.
        if (!_detector.IsSupported(filePath))
        {
            throw new DocumentException(
                $"\"{fileName}\" is an older file format that this application cannot read. Save it in the current format and try again.",
                ErrorCodes.DocumentFormatUnsupported);
        }

        return declared;
    }
}
