namespace WindowsAIAssistant.Core.Models.Documents;

/// <summary>
/// A chunk that was found to be relevant to a question, with the score that put it there.
/// <para>
/// The score is exposed rather than hidden so that a caller can explain a choice, and so a
/// test can assert that a chunk containing the answer outranks one that merely shares a
/// common word with the question.
/// </para>
/// </summary>
/// <param name="Chunk">The chunk that was considered.</param>
/// <param name="Score">
/// How well it matched. Zero means the question's terms did not appear in it at all.
/// </param>
public sealed record RankedDocumentChunk(DocumentChunk Chunk, double Score)
{
    /// <summary>Gets a value indicating whether the chunk shares no terms with the question.</summary>
    public bool IsMatch => Score > 0;
}
