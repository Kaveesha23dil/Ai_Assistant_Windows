using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Embeddings;

namespace WindowsAIAssistant.Core.Models.Knowledge;

/// <summary>
/// A passage a retriever found, with the scores that found it.
/// <para>
/// Both scores are kept, not just the winner. A passage that ranked high on wording and low on
/// meaning is a different kind of find from one that ranked high on both, and keeping only the
/// combined number would make that impossible to see when somebody asks why a result was
/// included. It is also what lets the retriever be tested on each half separately.
/// </para>
/// <para>
/// The vector is not carried. It is needed to produce the score and useless afterwards, so
/// keeping it would mean every result held a copy of a few kilobytes of numbers that nothing
/// would ever read — and a result object is the one thing in this application that reaches the
/// interface and the voice path.
/// </para>
/// </summary>
public sealed record KnowledgeSearchResult
{
    /// <summary>Gets the passage's identifier.</summary>
    public required Guid ChunkId { get; init; }

    /// <summary>Gets the document the passage came from.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Gets the file's name, without any directory part.</summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Gets the document family the passage came from.
    /// <para>
    /// Carried on the result rather than looked up, because it is how a caller tells whether a
    /// type filter was applied and how it labels a citation. A result that could not say which
    /// kind of file it came from would leave the interface describing a spreadsheet passage with
    /// no indication that it was one.
    /// </para>
    /// </summary>
    public DocumentFileType FileType { get; init; }

    /// <summary>Gets the base the document belongs to.</summary>
    public Guid KnowledgeBaseId { get; init; }

    /// <summary>Gets the passage's place in the document.</summary>
    public int Sequence { get; init; }

    /// <summary>Gets the section names, for display.</summary>
    public IReadOnlyList<string> Sections { get; init; } = [];

    /// <summary>Gets the text of the passage.</summary>
    public required string Text { get; init; }

    /// <summary>Gets how close the passage points to the question, from 1 to -1.</summary>
    /// <remarks>
    /// Negative when there is a vector score to report, and <see langword="null"/> when there was
    /// not — which is the case for a keyword-only search, where reporting zero would be
    /// indistinguishable from a passage that matched nothing.
    /// </remarks>
    public double VectorScore { get; init; } = double.NaN;

    /// <summary>Gets how much of the question's wording the passage shares, from 1 downwards.</summary>
    public double LexicalScore { get; init; }

    /// <summary>Gets the weighted total the two scores were ranked by.</summary>
    public double CombinedScore { get; init; }

    /// <summary>Gets where the passage came from, for a citation.</summary>
    public KnowledgeSourceReference SourceReference { get; init; } = new();

    /// <summary>Gets a value indicating whether a vector score was actually produced.</summary>
    public bool HasVectorScore => !double.IsNaN(VectorScore);

    /// <summary>
    /// Gets the phrase a citation should use, built only from what the document actually
    /// reported.
    /// </summary>
    public string ReferenceDisplay => SourceReference.HasAny ? SourceReference.Display : string.Empty;

    /// <summary>
    /// Gets the citation as a reader would see it, naming the file and — when there is one — the
    /// place inside it.
    /// </summary>
    public string CitationText =>
        string.IsNullOrEmpty(ReferenceDisplay) ? FileName : $"{FileName} \u2014 {ReferenceDisplay}";

    /// <summary>Gets the space the passage was embedded in, for a caller checking compatibility.</summary>
    public EmbeddingSpace? Space { get; init; }
}
