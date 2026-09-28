using WindowsAIAssistant.Core.Models.Documents;

namespace WindowsAIAssistant.Core.Abstractions.Documents;

/// <summary>
/// Divides a document into pieces small enough to send to an AI service one at a time.
/// <para>
/// The point is not to chop text up. It is that a request has a size limit, and a document
/// longer than one cannot be sent whole. A chunker that ignored the document's own structure
/// would still work and would produce chunks that each begin and end mid-sentence, which then
/// get summarized as though the fragment meant something on its own.
/// </para>
/// </summary>
public interface IDocumentChunker
{
    /// <summary>
    /// Splits a document into overlapping chunks, divided along the boundaries the format
    /// already has.
    /// </summary>
    /// <param name="content">The document to split.</param>
    /// <param name="maximumChunks">
    /// The most chunks to return. A document that would need more is cut down to this many
    /// rather than refused, because a partial answer beats none.
    /// </param>
    /// <param name="cancellationToken">Cancels the work.</param>
    Task<IReadOnlyList<DocumentChunk>> ChunkAsync(
        DocumentContent content,
        int maximumChunks,
        CancellationToken cancellationToken = default);
}
