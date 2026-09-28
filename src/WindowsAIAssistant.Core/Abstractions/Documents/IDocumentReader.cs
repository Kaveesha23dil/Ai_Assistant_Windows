using WindowsAIAssistant.Core.Models.Documents;

namespace WindowsAIAssistant.Core.Abstractions.Documents;

/// <summary>
/// Reads a document from disk and hands back its contents, with everything that has to happen
/// before the text is any good already done.
/// <para>
/// This is the one entry point the rest of the application should use. Validating the file,
/// checking its size, choosing a reader, and normalizing what comes back are all easy to forget
/// when they are left to each caller, and each of them is a case where forgetting is silent:
/// a path that does not exist reads as an empty document rather than a missing one.
/// </para>
/// </summary>
public interface IDocumentReader
{
    /// <summary>
    /// Reads a document and returns its contents.
    /// </summary>
    /// <exception cref="Exceptions.DocumentException">
    /// Thrown when the file is missing, unreadable, too large, in a format this build does not
    /// read, protected, or holds nothing but images. The code says which.
    /// </exception>
    Task<DocumentContent> ReadAsync(string filePath, CancellationToken cancellationToken = default);
}
