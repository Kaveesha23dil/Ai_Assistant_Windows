using WindowsAIAssistant.Core.Common;

namespace WindowsAIAssistant.Core.Models.Documents;

/// <summary>
/// A portion of a document, small enough to be sent on its own, still attached to where it
/// came from.
/// <para>
/// The references are carried with every chunk rather than worked out afterwards. A chunk that
/// has lost its page number can still inform an answer, but it can no longer be cited, and an
/// answer nobody can check against the document is worth much less than one they can.
/// </para>
/// </summary>
public sealed record DocumentChunk
{
    /// <summary>Gets the chunk's identifier, unique within one chunking run.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the chunk's place in the document, counting from zero.</summary>
    public int Sequence { get; init; }

    /// <summary>Gets the text of the chunk.</summary>
    public required string Text { get; init; }

    /// <summary>Gets the names of the sections this chunk was taken from.</summary>
    public IReadOnlyList<string> Sections { get; init; } = [];

    /// <summary>
    /// Gets the citation for the first part of the chunk, which is the one an answer would
    /// point at.
    /// </summary>
    public string StartReference { get; init; } = string.Empty;

    /// <summary>Gets the citation for the last part of the chunk.</summary>
    public string EndReference { get; init; } = string.Empty;

    /// <summary>Gets the number of characters in the chunk.</summary>
    public int CharacterCount => Text.Length;

    /// <summary>Gets an estimate of how many tokens the chunk is worth.</summary>
    public int EstimatedTokenCount => TokenEstimator.EstimateTokens(Text);

    /// <summary>
    /// Gets the citation an answer should use. A chunk taken from a single page is cited as
    /// that page; a chunk spanning several is cited as the span it covers, so a reader is not
    /// sent to one place for text that is not there.
    /// </summary>
    public string Reference => string.IsNullOrWhiteSpace(StartReference)
        ? EndReference
        : string.Equals(StartReference, EndReference, StringComparison.OrdinalIgnoreCase)
            ? StartReference
            : $"{StartReference} to {EndReference}";

    /// <summary>Gets the section names as one line, for showing beside an answer.</summary>
    public string SectionSummary => string.Join(", ", Sections);
}
