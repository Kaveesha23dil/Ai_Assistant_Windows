using WindowsAIAssistant.Core.Abstractions.Documents;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;

namespace WindowsAIAssistant.Infrastructure.Documents;

/// <summary>
/// Keeps the readers and hands out the one that can open a given document.
/// <para>
/// The readers are found from the service collection rather than named here, so registering an
/// extractor is the whole of adding support for a format. Nothing in this class can fall out of
/// step with what is actually registered, which is the failure a hand-written switch statement
/// would eventually have.
/// </para>
/// </summary>
public sealed class DocumentExtractorFactory : IDocumentExtractorFactory
{
    private readonly Dictionary<DocumentFileType, IDocumentExtractor> _extractors;
    private readonly IDocumentTypeDetector _detector;

    public DocumentExtractorFactory(
        IEnumerable<IDocumentExtractor> extractors,
        IDocumentTypeDetector detector)
    {
        ArgumentNullException.ThrowIfNull(extractors);
        ArgumentNullException.ThrowIfNull(detector);

        _detector = detector;
        _extractors = [];

        foreach (var extractor in extractors)
        {
            // The first registration for a format wins, so a differently configured reader
            // cannot quietly displace the one the composition root chose.
            _extractors.TryAdd(extractor.FileType, extractor);
        }
    }

    /// <inheritdoc />
    public IDocumentExtractor GetExtractor(DocumentFileType fileType)
    {
        if (_extractors.TryGetValue(fileType, out var extractor))
        {
            return extractor;
        }

        throw new DocumentException(
            "This application cannot read that kind of document yet.",
            ErrorCodes.DocumentFormatUnsupported);
    }

    /// <inheritdoc />
    public IDocumentExtractor GetExtractorFor(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fileType = _detector.Detect(filePath);
        if (fileType is DocumentFileType.Unknown)
        {
            var extension = Path.GetExtension(filePath);
            var name = Path.GetFileName(filePath);

            throw new DocumentException(
                extension.Length > 0
                    ? $"\"{name}\" is a {extension} file, which this application cannot read yet."
                    : $"\"{name}\" has no file extension, so this application cannot tell what kind of document it is.",
                ErrorCodes.DocumentFormatUnsupported);
        }

        return GetExtractor(fileType);
    }
}
