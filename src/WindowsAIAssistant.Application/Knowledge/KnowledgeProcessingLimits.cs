namespace WindowsAIAssistant.Application.Knowledge;

/// <summary>
/// The bounds of the knowledge store, as one value owned by this layer.
/// <para>
/// A knowledge base grows one file at a time without anybody deciding when to stop, so every
/// value here bounds something. The configuration type belongs to Infrastructure; this is the
/// application's copy, mapped once at startup, so indexing and retrieval take a fixed value
/// rather than reading configuration per file — a document read halfway through cannot have its
/// maximum changed under it by a settings save in another view.
/// </para>
/// <para>
/// Only a file name appears here, never a path. Where the store lives is worked out by the
/// component that opens it, from the platform's own per-user application data directory, which is
/// what keeps a person's account name and folder layout out of anything that gets committed or
/// copied between machines.
/// </para>
/// </summary>
public sealed record KnowledgeProcessingLimits
{
    /// <summary>Gets whether the knowledge base is offered at all.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Gets the store's file name, resolved under the per-user application data directory.
    /// <para>
    /// Known to the indexing service for one reason: indexing a folder must skip the store
    /// itself, or the index would read its own database, find no text in it, and report a
    /// document that does not exist.
    /// </para>
    /// </summary>
    public string DatabaseFileName { get; init; } = "knowledge.db";

    /// <summary>Gets the most documents that will be indexed across all bases.</summary>
    public int MaximumDocuments { get; init; } = 1_000;

    /// <summary>
    /// Gets the most passages stored for one document. A document needing more is truncated and
    /// the person is told, rather than refused, because a partial index is useful and no index
    /// is not.
    /// </summary>
    public int MaximumChunksPerDocument { get; init; } = 5_000;

    /// <summary>Gets how many passages a question retrieves when nothing else is specified.</summary>
    public int DefaultRetrievalCount { get; init; } = 8;

    /// <summary>
    /// Gets the most passages one question may retrieve. More context is not better context: past
    /// a point the relevant passages are crowded out by merely-adjacent ones, and the model
    /// spends its attention on material that does not answer the question.
    /// </summary>
    public int MaximumRetrievalCount { get; init; } = 20;

    /// <summary>
    /// Gets the most passages a single scan will read from storage.
    /// <para>
    /// A deliberate cap on the scan rather than a limit on what is stored. Reading every passage
    /// in a large base for one question would grow without bound and would eventually stop
    /// answering, so the scan reads the most recently indexed passages, which is where the
    /// material a person has been working with actually is. Raising this trades memory and time
    /// for recall across a larger base.
    /// </para>
    /// </summary>
    public int MaximumScannedChunks { get; init; } = 20_000;
}
