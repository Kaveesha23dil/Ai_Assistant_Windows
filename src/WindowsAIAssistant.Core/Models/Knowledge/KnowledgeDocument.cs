using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Embeddings;

namespace WindowsAIAssistant.Core.Models.Knowledge;

/// <summary>
/// One file in a knowledge base, and how far it has got.
/// <para>
/// This is the record a person sees in a list, so it carries the things they act on — name,
/// size, status, how many chunks, when it was indexed — and the identity fields a search needs.
/// It deliberately does not carry the document's text: the text lives in the chunks, and holding
/// a second copy of a whole document on a row that is only ever listed would double the memory
/// the page costs and give one more place for it to be logged by accident.
/// </para>
/// <para>
/// The file's content hash and its modification date are both kept because they answer different
/// questions. The hash answers "is this the same document", which is what duplicate detection
/// and change detection need, and the date answers "is there any reason to check", which is a
/// cheap first filter for a person with a hundred files.
/// </para>
/// </summary>
public sealed record KnowledgeDocument
{
    /// <summary>Gets the identifier, stable for the life of the record.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the base this document belongs to.</summary>
    public required Guid KnowledgeBaseId { get; init; }

    /// <summary>Gets the file's name, without any directory part.</summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Gets where the file is on this machine.
    /// <para>
    /// Held so the file can be reopened, re-read, and checked for changes without asking the
    /// person to find it again. It is never sent to a provider and never logged: an answer that
    /// mentioned a person's full folder path would tell the model — and anybody reading the
    /// transcript — where their files live, which is not what anybody asked for.
    /// </para>
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>Gets which family of document this is.</summary>
    public DocumentFileType FileType { get; init; }

    /// <summary>Gets the size on disk, in bytes.</summary>
    public long FileSize { get; init; }

    /// <summary>
    /// Gets the SHA-256 of the file's bytes, in lower-case hex.
    /// <para>
    /// The name is not used for this, deliberately. Two copies of the same document with
    /// different names are the same document, and one document renamed is the same document, so
    /// an identity built from anything the person can type will be wrong about one of those.
    /// </para>
    /// </summary>
    public required string FileHash { get; init; }

    /// <summary>Gets when the file was last written, as of indexing.</summary>
    public DateTimeOffset? ModifiedAt { get; init; }

    /// <summary>Gets when the file was indexed, or <see langword="null"/> if it never was.</summary>
    public DateTimeOffset? IndexedAt { get; init; }

    /// <summary>Gets how many chunks are indexed for this document.</summary>
    public int ChunkCount { get; init; }

    /// <summary>Gets how far the document has got.</summary>
    public KnowledgeDocumentStatus Status { get; init; }

    /// <summary>
    /// Gets the note explaining the status, for a failure or a warning.
    /// <para>
    /// Written for a person: no file path, no library name, no chunk text. It is shown in the
    /// document list, so it has to be safe to display anywhere the row is.
    /// </para>
    /// </summary>
    public string? StatusMessage { get; init; }

    /// <summary>
    /// Gets the embedding space the stored vectors belong to, or <see langword="null"/> for a
    /// document that has never been embedded.
    /// <para>
    /// Stored per document rather than once for the whole index so that changing the model can
    /// mark exactly the documents that need reindexing, and leave the rest alone. A person who
    /// changed the model on a Tuesday should not be told on Wednesday that everything is broken
    /// when only the documents added in between are.
    /// </para>
    /// </summary>
    public EmbeddingSpace? EmbeddingSpace { get; init; }

    /// <summary>Gets when the record itself was created.</summary>
    public DateTimeOffset AddedAt { get; init; }

    /// <summary>
    /// Gets a value indicating whether the file on disk still matches what was indexed.
    /// <para>
    /// Both the date and the hash are asked about, because they fail in different ways. The date
    /// catches an ordinary edit cheaply. The hash catches the case the date cannot: a file
    /// replaced with an older copy, or edited and given back its original timestamp.
    /// </para>
    /// </summary>
    public bool Matches(DateTimeOffset? modifiedAt, string fileHash) =>
        string.Equals(FileHash, fileHash, StringComparison.OrdinalIgnoreCase)
        && (ModifiedAt is null || modifiedAt is null
            || Math.Abs((ModifiedAt.Value - modifiedAt.Value).TotalSeconds) < 2d);
}
