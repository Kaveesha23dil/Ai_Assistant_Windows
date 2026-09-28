using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Documents;

namespace WindowsAIAssistant.Core.Abstractions.Documents;

/// <summary>
/// Finds the reader that can open a given kind of document.
/// </summary>
public interface IDocumentExtractorFactory
{
    /// <summary>
    /// Returns the reader for a family of document.
    /// </summary>
    /// <exception cref="Exceptions.DocumentException">
    /// Thrown with <see cref="Common.ErrorCodes.DocumentFormatUnsupported"/> when no reader is
    /// registered, so the caller learns the format is not read rather than that a service was
    /// missing.
    /// </exception>
    IDocumentExtractor GetExtractor(DocumentFileType fileType);

    /// <summary>
    /// Returns the reader for a file, working out its type first.
    /// </summary>
    /// <exception cref="Exceptions.DocumentException">
    /// Thrown when the file is not a kind of document this build reads.
    /// </exception>
    IDocumentExtractor GetExtractorFor(string filePath);
}
