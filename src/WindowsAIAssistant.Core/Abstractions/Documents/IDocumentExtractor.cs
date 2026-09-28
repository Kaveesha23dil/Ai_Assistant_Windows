using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Documents;

namespace WindowsAIAssistant.Core.Abstractions.Documents;

/// <summary>
/// Reads one kind of document into text.
/// <para>
/// An extractor is asked whether it handles a file rather than being told to: the factory asks
/// every registered extractor, and a file nobody claims is a format this build does not read.
/// That keeps adding a format to adding a registration, with no list to keep in step.
/// </para>
/// </summary>
public interface IDocumentExtractor
{
    /// <summary>Gets the family of document this extractor reads.</summary>
    DocumentFileType FileType { get; }

    /// <summary>
    /// Gets a value indicating whether this extractor claims the given file. Called with the
    /// lower-case extension, including the leading dot.
    /// </summary>
    bool CanExtract(string extension);

    /// <summary>
    /// Reads the document.
    /// <para>
    /// The path arrives as given and the caller holds it; nothing here is permitted to keep
    /// it, log it, or put it in a result. What comes back carries the file's name only.
    /// </para>
    /// </summary>
    /// <exception cref="Exceptions.DocumentException">
    /// Thrown when the document cannot be read, carrying a code that says why.
    /// </exception>
    Task<DocumentContent> ExtractAsync(string filePath, CancellationToken cancellationToken = default);
}
