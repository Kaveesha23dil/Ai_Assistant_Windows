namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// How far a document has got through being added to a knowledge base.
/// <para>
/// Every stage a document can be seen at is a value here rather than a set of booleans, so the
/// page can show one truthful thing about a row. A record with <c>IsIndexed</c> and
/// <c>IsFailed</c> both true has no meaning to show a person, and one of the two is always
/// wrong.
/// </para>
/// <para>
/// The order of the values is the order work happens in, which lets progress be compared with
/// <c>&gt;=</c> and lets a row be shown as "in progress" by asking a single question.
/// </para>
/// </summary>
public enum KnowledgeDocumentStatus
{
    /// <summary>
    /// Accepted but not started, or stopped part-way. A cancelled document lands here rather
    /// than being marked indexed or deleted, so the person can see it and start it again.
    /// </summary>
    Pending = 0,

    /// <summary>The file is being read and its text extracted.</summary>
    Reading = 1,

    /// <summary>The extracted text is being split into chunks.</summary>
    Chunking = 2,

    /// <summary>Chunks are being sent to the embedding provider.</summary>
    Embedding = 3,

    /// <summary>
    /// Fully indexed and searchable. Only reached once the chunks and their vectors are
    /// committed, never before, so a search can never retrieve from a document that is
    /// half-written.
    /// </summary>
    Indexed = 4,

    /// <summary>
    /// Indexed once, but the file on disk has changed since. Its indexed text is still
    /// searchable and still true of the older file, and the person decides when to reindex.
    /// </summary>
    Outdated = 5,

    /// <summary>
    /// Indexed, but the embedding model has changed since, so its vectors cannot be compared
    /// with anything produced since. The content is not lost; it must be embedded again.
    /// </summary>
    ReindexRequired = 6,

    /// <summary>Something went wrong. The reason is kept in the document's status note.</summary>
    Failed = 7,

    /// <summary>
    /// Removed from the knowledge base. The local file is untouched, which is the whole point of
    /// removing rather than deleting.
    /// </summary>
    Removed = 8,
}

/// <summary>
/// Shared questions about <see cref="KnowledgeDocumentStatus"/> that more than one layer
/// needs answered the same way.
/// </summary>
public static class KnowledgeDocumentStatuses
{
    /// <summary>
    /// Gets a value indicating whether a document in this state is one a search may return.
    /// <para>
    /// <see cref="KnowledgeDocumentStatus.Outdated"/> counts. Its text is still indexed and
    /// still answers questions, and refusing it would make a person reindex a whole base because
    /// one file moved. Only documents that are not fully written, or that cannot be compared in
    /// the current embedding space, are excluded.
    /// </para>
    /// </summary>
    public static bool IsSearchable(this KnowledgeDocumentStatus status) => status is
        KnowledgeDocumentStatus.Indexed or KnowledgeDocumentStatus.Outdated;

    /// <summary>
    /// Gets a value indicating whether a document in this state is being worked on right now,
    /// and so must not be started a second time.
    /// </summary>
    public static bool IsInProgress(this KnowledgeDocumentStatus status) => status is
        KnowledgeDocumentStatus.Reading
        or KnowledgeDocumentStatus.Chunking
        or KnowledgeDocumentStatus.Embedding;

    /// <summary>
    /// Gets a value indicating whether a document in this state has no usable content in the
    /// index, and so contributes nothing to a search.
    /// </summary>
    public static bool HasNoIndexedContent(this KnowledgeDocumentStatus status) => !status.IsSearchable();
}
