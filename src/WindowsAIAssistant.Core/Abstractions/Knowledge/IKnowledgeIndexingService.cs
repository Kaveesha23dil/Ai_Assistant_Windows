using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Knowledge;

namespace WindowsAIAssistant.Core.Abstractions.Knowledge;

/// <summary>
/// Adds files to a knowledge base, re-adds them when they change, and takes them out again.
/// <para>
/// The whole indexing pipeline sits behind this one interface, for the same reason document
/// reading did: validation, reading, chunking, embedding, the permission check, the transaction,
/// and the status a person sees are seven steps that must happen in that order every time, and
/// every caller that assembled its own version of them would eventually assemble one that
/// skipped the permission or marked a document indexed before the vectors were stored.
/// </para>
/// </summary>
public interface IKnowledgeIndexingService
{
    /// <summary>
    /// Gets a value indicating whether a file is already being indexed in this process.
    /// <para>
    /// Checked before starting, because two runs over one document write to the same rows and the
    /// second would either duplicate them or delete the first's work part-way through. Keyed by
    /// content hash rather than by path, so the same file added twice under two names is also
    /// recognised as the same job.
    /// </para>
    /// </summary>
    bool IsIndexing(string fileHash);

    /// <summary>Adds several files to a base, one after another, reporting progress as it goes.</summary>
    /// <param name="request">Which files, into which base, and where progress is reported.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    Task<IReadOnlyList<KnowledgeIndexingResult>> IndexAsync(
        KnowledgeIndexingRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Rebuilds one document's index from the file on disk, replacing what was there.</summary>
    Task<KnowledgeIndexingResult> ReindexAsync(
        Guid documentId,
        IProgress<KnowledgeIndexingProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks the files on disk against what was indexed and marks the ones that changed as
    /// outdated. Nothing is reindexed: the person decides when.
    /// </summary>
    Task<IReadOnlyList<KnowledgeDocument>> RefreshAsync(
        Guid knowledgeBaseId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks every document whose vectors were produced by a model other than the current one
    /// as needing a reindex, and returns how many.
    /// </summary>
    Task<int> MarkStaleEmbeddingsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// One file to add, and where it should go.
/// </summary>
/// <param name="FilePath">The file to index. Validated by the service, not by the caller.</param>
public sealed record KnowledgeIndexingRequestItem(string FilePath);

/// <summary>
/// A request to add files to a base.
/// <para>
/// Files are processed one after another rather than concurrently, on purpose. Each one reads a
/// file, chunks it, and sends a batch of chunks to an embedding provider, so running several at
/// once means several sets of those at once against a service that rate-limits requests and
/// charges for them. Adding twenty documents should take a while, visibly, and report where it
/// has got to — not open twenty connections and fail.
/// </para>
/// </summary>
public sealed record KnowledgeIndexingRequest
{
    /// <summary>Gets the base to add to.</summary>
    public required Guid KnowledgeBaseId { get; init; }

    /// <summary>Gets the files to add, in the order they should be processed.</summary>
    public required IReadOnlyList<KnowledgeIndexingRequestItem> Items { get; init; }

    /// <summary>Gets where progress is reported, or <see langword="null"/> for none.</summary>
    public IProgress<KnowledgeIndexingProgress>? Progress { get; init; }

    /// <summary>Creates a request from a set of paths.</summary>
    public static KnowledgeIndexingRequest Create(
        Guid knowledgeBaseId,
        IEnumerable<string> filePaths,
        IProgress<KnowledgeIndexingProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        var items = filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new KnowledgeIndexingRequestItem(path))
            .ToArray();

        return new KnowledgeIndexingRequest
        {
            KnowledgeBaseId = knowledgeBaseId,
            Items = items,
            Progress = progress,
        };
    }
}

/// <summary>
/// What indexing is doing, described in words a person can read.
/// <para>
/// Counts, not percentages. A percentage would have to be a guess about how much of reading a
/// 40-page PDF remains, and a progress bar that sits at 90% for two minutes teaches people that
/// the numbers on it are decorative. A file name, a stage, and a real count of "12 of 47" are
/// all that can be stated honestly.
/// </para>
/// </summary>
/// <param name="FileName">The file being worked on. Its name, never its path.</param>
/// <param name="Stage">What is happening now.</param>
/// <param name="Completed">How many of the expected units are done.</param>
/// <param name="Total">How many there are in total, when that is known.</param>
public sealed record KnowledgeIndexingProgress(
    string FileName,
    KnowledgeIndexingStage Stage,
    int Completed = 0,
    int Total = 0)
{
    /// <summary>Gets the sentence shown while indexing.</summary>
    public string Message => (Stage, Completed, Total) switch
    {
        (KnowledgeIndexingStage.Reading, _, _) => $"Reading {FileName}...",
        (KnowledgeIndexingStage.Splitting, _, _) => $"Splitting {FileName}...",
        (KnowledgeIndexingStage.Embedding, 0, 0) => $"Generating embeddings for {FileName}...",
        (KnowledgeIndexingStage.Embedding, var done, var total) => $"Generating embeddings {done} / {total}...",
        (KnowledgeIndexingStage.Saving, _, _) => $"Saving {FileName}...",
        (KnowledgeIndexingStage.Completed, _, _) => $"{FileName} is indexed.",
        (KnowledgeIndexingStage.Failed, _, _) => $"{FileName} could not be indexed.",
        (KnowledgeIndexingStage.Cancelled, _, _) => $"Indexing {FileName} was stopped.",
        _ => $"Working on {FileName}...",
    };
}

/// <summary>The stages of indexing, in the order they happen.</summary>
public enum KnowledgeIndexingStage
{
    /// <summary>The file is being read and its text extracted.</summary>
    Reading,

    /// <summary>The text is being split into passages.</summary>
    Splitting,

    /// <summary>Passages are being sent to the embedding provider.</summary>
    Embedding,

    /// <summary>Passages and vectors are being written to the index.</summary>
    Saving,

    /// <summary>The document is fully indexed.</summary>
    Completed,

    /// <summary>The document could not be indexed.</summary>
    Failed,

    /// <summary>Indexing was stopped part-way.</summary>
    Cancelled,
}

/// <summary>
/// What happened to one file.
/// <para>
/// A failure is a result rather than an exception, because adding twenty files and having the
/// nineteenth fail must not lose the eighteen that worked, nor leave the person not knowing
/// which was which.
/// </para>
/// </summary>
public sealed record KnowledgeIndexingResult
{
    /// <summary>Gets the file that was attempted, named rather than addressed.</summary>
    public required string FileName { get; init; }

    /// <summary>Gets the document record, when one exists now.</summary>
    public Guid? DocumentId { get; init; }

    /// <summary>Gets whether the file is now indexed and searchable.</summary>
    public bool IsIndexed { get; init; }

    /// <summary>
    /// Gets whether the file was already indexed and nothing was done, which is a success of a
    /// kind: the person asked for it to be there and it is.
    /// </summary>
    public bool WasAlreadyIndexed { get; init; }

    /// <summary>Gets how many passages the document now has.</summary>
    public int ChunkCount { get; init; }

    /// <summary>Gets the stable code when the attempt failed.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Gets the sentence to show when the attempt failed.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Gets whether the attempt failed.</summary>
    public bool IsSuccess => IsIndexed || WasAlreadyIndexed;

    /// <summary>Records a document that is indexed.</summary>
    public static KnowledgeIndexingResult Success(string fileName, Guid documentId, int chunkCount) =>
        new()
        {
            FileName = fileName,
            DocumentId = documentId,
            IsIndexed = true,
            ChunkCount = chunkCount,
        };

    /// <summary>Records a file that was already there.</summary>
    public static KnowledgeIndexingResult AlreadyIndexed(string fileName, Guid documentId, int chunkCount) =>
        new()
        {
            FileName = fileName,
            DocumentId = documentId,
            WasAlreadyIndexed = true,
            ChunkCount = chunkCount,
        };

    /// <summary>Records a failure.</summary>
    public static KnowledgeIndexingResult Failure(string fileName, string errorCode, string message) =>
        new()
        {
            FileName = fileName,
            ErrorCode = errorCode,
            ErrorMessage = message,
        };
}
