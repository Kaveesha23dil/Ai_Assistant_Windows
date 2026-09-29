namespace WindowsAIAssistant.Infrastructure.Configuration.Options;

/// <summary>
/// The knowledge-base configuration, bound from the <c>KnowledgeBase</c> section.
/// <para>
/// Every value here bounds something. A knowledge base grows one file at a time, without anybody
/// deciding when to stop, so the limits that exist are the ones that keep a person who indexes
/// their entire document folder from ending up with a store too large to search in reasonable
/// time, or too large to open.
/// </para>
/// <para>
/// Only a file name is configured, never a path. The location is worked out from the platform's
/// own per-user application data directory at the point the database is opened, which is what
/// keeps a person's account name and folder layout out of a file that gets committed and copied
/// between machines.
/// </para>
/// </summary>
public sealed class KnowledgeBaseOptions
{
    public const string SectionName = "KnowledgeBase";

    /// <summary>Gets whether the knowledge base is offered at all.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Gets the database's file name, resolved under the per-user application data directory.
    /// A value containing a path separator is refused, because a configuration file that could
    /// point the database at <c>C:\Windows\</c> is a configuration file nobody should ship.
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
    /// Gets the most passages a single vector scan will read from storage.
    /// <para>
    /// A deliberate cap on the scan rather than a limit on what is stored. Loading every vector
    /// in a large base for one question would grow without bound and would eventually stop
    /// answering, so the scan takes the most recently indexed passages, which is where the
    /// material a person has been working with actually is. Raising this trades memory and time
    /// for recall across a larger base.
    /// </para>
    /// </summary>
    public int MaximumScannedChunks { get; init; } = 20_000;
}
