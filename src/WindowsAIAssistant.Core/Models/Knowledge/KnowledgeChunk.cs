using WindowsAIAssistant.Core.Models.Embeddings;

namespace WindowsAIAssistant.Core.Models.Knowledge;

/// <summary>
/// One indexed passage of one document, with the vector that makes it findable.
/// <para>
/// The text and the vector are kept on the same record deliberately. They are written together,
/// read together, and are meaningless apart: a vector without its text cannot be shown, and text
/// without its vector cannot be ranked. Splitting them across two tables would buy a little
/// normalisation and cost a join on the hottest path in the feature.
/// </para>
/// <para>
/// The whole document is never repeated here. A chunk holds only its own text, so the cost of
/// indexing a book is the book's own size plus the cost of its vectors, and adding one
/// knowledge base to another would not duplicate a megabyte.
/// </para>
/// <para>
/// The content hash is what makes reindexing cheap. A passage that has not changed keeps its
/// vector across a reindex, so re-reading a document that was edited in one place costs one
/// embedding request rather than one per chunk — which, for a cloud provider, is the difference
/// between a reindex and a bill.
/// </para>
/// </summary>
public sealed record KnowledgeChunk
{
    /// <summary>Gets the identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the document this passage came from.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Gets the base the document belongs to, denormalized so a search need not join.</summary>
    public required Guid KnowledgeBaseId { get; init; }

    /// <summary>Gets the passage's place in the document, counting from zero.</summary>
    public int Sequence { get; init; }

    /// <summary>Gets the text of the passage.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// Gets the section names the passage was taken from, as the extractor named them.
    /// </summary>
    public IReadOnlyList<string> Sections { get; init; } = [];

    /// <summary>Gets where the passage came from, for a citation.</summary>
    public KnowledgeSourceReference SourceReference { get; init; } = new();

    /// <summary>Gets the number of characters in the passage.</summary>
    public int CharacterCount { get; init; }

    /// <summary>
    /// Gets the vector that makes the passage findable, or <see langword="null"/> while the
    /// document is still being embedded.
    /// </summary>
    public EmbeddingVector? Embedding { get; init; }

    /// <summary>
    /// Gets the SHA-256 of the passage's text, in lower-case hex.
    /// <para>
    /// The cache key for the embedding. Deliberately not keyed on anything else: two passages
    /// with the same text embed to the same vector, and whether they happen to sit at the same
    /// place in the same document is irrelevant to that.
    /// </para>
    /// </summary>
    public required string ContentHash { get; init; }

    /// <summary>Gets the space the vector belongs to, or <see langword="null"/> if not embedded yet.</summary>
    public EmbeddingSpace? EmbeddingSpace => Embedding?.Space;

    /// <summary>Returns the passage with a different vector attached, for the write path.</summary>
    public KnowledgeChunk WithEmbedding(EmbeddingVector? vector) => this with { Embedding = vector };

    /// <summary>Copies the passage under a new identifier, for a document being written afresh.</summary>
    public KnowledgeChunk WithIdentity(Guid documentId, Guid knowledgeBaseId, int sequence) =>
        this with
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            KnowledgeBaseId = knowledgeBaseId,
            Sequence = sequence,
        };
}
