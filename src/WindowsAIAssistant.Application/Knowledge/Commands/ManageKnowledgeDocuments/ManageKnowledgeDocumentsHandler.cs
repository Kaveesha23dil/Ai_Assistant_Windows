using WindowsAIAssistant.Core.Abstractions.Documents;
using WindowsAIAssistant.Core.Abstractions.Knowledge;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Knowledge;

namespace WindowsAIAssistant.Application.Knowledge.Commands.ManageKnowledgeDocuments;

/// <summary>
/// How far along one file is, as the page shows it.
/// <para>
/// The file is named, never its path, for the reason the index does the same: a status line is
/// read by people and written to logs, and neither needs to know where somebody keeps their
/// files.
/// </para>
/// </summary>
public sealed record KnowledgeIndexingProgressDto
{
    public required string FileName { get; init; }

    public required KnowledgeIndexingStage Stage { get; init; }

    public int Completed { get; init; }

    public int Total { get; init; }

    /// <summary>Gets a value indicating whether a count is worth showing at all.</summary>
    public bool HasCount => Total > 0;

    /// <summary>Gets the sentence the page puts in front of a person.</summary>
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

/// <summary>What happened to the files in one request.</summary>
public sealed record KnowledgeIndexingReport
{
    public required int Indexed { get; init; }

    public required int AlreadyIndexed { get; init; }

    public required IReadOnlyList<KnowledgeIndexingFailure> Failures { get; init; }

    public int Total => Indexed + AlreadyIndexed + Failures.Count;

    public bool IsComplete => Failures.Count == 0;

    public string Summary => Total switch
    {
        0 => "Nothing was selected to index.",
        1 => Failures.Count == 0
            ? AlreadyIndexed == 1 ? "That file is already indexed." : "1 file is indexed."
            : "1 file could not be indexed.",
        _ => $"{Indexed} indexed, {AlreadyIndexed} already indexed, {Failures.Count} could not be indexed.",
    };
}

/// <summary>One file that could not be indexed, and why in words a person can act on.</summary>
public sealed record KnowledgeIndexingFailure(string FileName, string ErrorCode, string Message);

/// <summary>
/// Adds, removes, and reindexes documents.
/// <para>
/// Everything here goes through <see cref="IKnowledgeIndexingService"/> rather than touching the
/// repositories directly, because the steps — validate, hash, decide, read, split, embed, write —
/// are what make a document safe to search, and a caller that skipped one of them would leave a
/// document in the index that could not be reproduced from its file.
/// </para>
/// </summary>
public sealed class ManageKnowledgeDocumentsHandler
{
    private readonly IKnowledgeIndexingService _indexing;
    private readonly IKnowledgeDocumentRepository _documents;
    private readonly IDocumentTypeDetector _types;

    public ManageKnowledgeDocumentsHandler(
        IKnowledgeIndexingService indexing,
        IKnowledgeDocumentRepository documents,
        IDocumentTypeDetector types)
    {
        ArgumentNullException.ThrowIfNull(indexing);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(types);

        _indexing = indexing;
        _documents = documents;
        _types = types;
    }

    /// <summary>
    /// Adds files to a base, one at a time, reporting where it has got to.
    /// <para>
    /// Files are enumerated before any of them is sent, so an unsupported file in the selection
    /// is skipped with a reason rather than turning the whole request into a failure after the
    /// documents before it were already indexed and paid for.
    /// </para>
    /// </summary>
    public async Task<KnowledgeIndexingReport> AddFilesAsync(
        Guid knowledgeBaseId,
        IReadOnlyList<string> filePaths,
        IProgress<KnowledgeIndexingProgressDto>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        cancellationToken.ThrowIfCancellationRequested();

        var failures = new List<KnowledgeIndexingFailure>();
        var indexed = 0;
        var unchanged = 0;

        var (pathFailures, rejected) = ValidatePaths(filePaths);

        if (pathFailures.Count > 0)
        {
            failures.AddRange(pathFailures);
        }

        // Only the paths that passed are handed on. Passing the original list would have the
        // indexing service discover the same missing file a second time and report it a second
        // time, in words meant for a different failure.
        var indexable = filePaths.Where(path => !rejected.Contains(path)).ToArray();

        if (indexable.Length == 0)
        {
            return new KnowledgeIndexingReport
            {
                Indexed = 0,
                AlreadyIndexed = 0,
                Failures = failures,
            };
        }

        var results = await _indexing.IndexAsync(
            KnowledgeIndexingRequest.Create(knowledgeBaseId, indexable, Adapt(progress)),
            cancellationToken).ConfigureAwait(false);

        foreach (var result in results)
        {
            if (result.IsSuccess)
            {
                if (result.WasAlreadyIndexed)
                {
                    unchanged++;
                }
                else
                {
                    indexed++;
                }

                continue;
            }

            failures.Add(new KnowledgeIndexingFailure(
                result.FileName,
                result.ErrorCode ?? ErrorCodes.KnowledgeOperationFailed,
                result.ErrorMessage ?? "That file could not be indexed."));
        }

        return new KnowledgeIndexingReport
        {
            Indexed = indexed,
            AlreadyIndexed = unchanged,
            Failures = failures,
        };
    }

    /// <summary>
    /// Adds every supported document in a folder.
    /// <para>
    /// The files are listed when the request is made rather than watched afterwards. Watching a
    /// folder means reindexing a document every time somebody saves it over the top of itself,
    /// which spends a provider request per passage on a file that has not meaningfully changed —
    /// and the change detection that does exist is a hash of the contents, not of the clock.
    /// </para>
    /// </summary>
    public Task<KnowledgeIndexingReport> AddFolderAsync(
        Guid knowledgeBaseId,
        string folderPath,
        IProgress<KnowledgeIndexingProgressDto>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        if (!Directory.Exists(folderPath))
        {
            return Task.FromResult(new KnowledgeIndexingReport
            {
                Indexed = 0,
                AlreadyIndexed = 0,
                Failures =
                [
                new KnowledgeIndexingFailure(
                    new DirectoryInfo(folderPath).Name,
                    ErrorCodes.DocumentNotFound,
                    "That folder could not be found."),
                ],
            });
        }

        var candidates = Directory
            .EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly)
            .Where(path => _types.SupportedExtensions.Contains(
                Path.GetExtension(path),
                StringComparer.OrdinalIgnoreCase))
            .ToArray();

        var rejected = Directory
            .EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly)
            .Where(path => !_types.SupportedExtensions.Contains(
                Path.GetExtension(path),
                StringComparer.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => new KnowledgeIndexingFailure(
                name!,
                ErrorCodes.DocumentFormatUnsupported,
                "That kind of file cannot be indexed."))
            .ToArray();

        return AddFilesAsync(knowledgeBaseId, candidates, progress, cancellationToken)
            .ContinueWith(
                completed => WithExtraFailures(completed.Result, rejected),
                cancellationToken,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }

    /// <summary>
    /// Rebuilds one document's index from the file on disk, replacing what was there.
    /// <para>
    /// The replacement is a delete and a write inside one transaction, so a question answered
    /// while the rebuild runs gets either the old passages or the new ones and never a
    /// half-replaced document.
    /// </para>
    /// </summary>
    public async Task<KnowledgeIndexingReport> ReindexAsync(
        Guid documentId,
        IProgress<KnowledgeIndexingProgressDto>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = await _indexing.ReindexAsync(documentId, Adapt(progress), cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            return new KnowledgeIndexingReport
            {
                Indexed = result.WasAlreadyIndexed ? 0 : 1,
                AlreadyIndexed = result.WasAlreadyIndexed ? 1 : 0,
                Failures = [],
            };
        }

        return new KnowledgeIndexingReport
        {
            Indexed = 0,
            AlreadyIndexed = 0,
            Failures =
            [
                new KnowledgeIndexingFailure(
                    result.FileName,
                    result.ErrorCode ?? ErrorCodes.KnowledgeOperationFailed,
                    result.ErrorMessage ?? "That file could not be reindexed."),
            ],
        };
    }

    /// <summary>
    /// Takes a document out of a base. The file stays exactly where it is, which is the
    /// difference between removing a document and deleting one, and the confirmation says so.
    /// </summary>
    public Task<bool> RemoveAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        _documents.RemoveAsync(documentId, cancellationToken);

    /// <summary>
    /// Finds the documents whose file has changed since it was indexed, and marks them outdated
    /// without reindexing them.
    /// <para>
    /// Left as a separate step on purpose. Discovering a change is cheap and safe; rewriting an
    /// index costs a provider request per passage, and when to spend that is the person's call.
    /// </para>
    /// </summary>
    public Task<IReadOnlyList<KnowledgeDocument>> FindChangedAsync(
        Guid knowledgeBaseId,
        CancellationToken cancellationToken = default) =>
        _indexing.RefreshAsync(knowledgeBaseId, cancellationToken);

    /// <summary>
    /// Marks every document indexed in a different embedding space as needing a reindex, which is
    /// what a model change means for a store that already exists.
    /// </summary>
    public Task<int> MarkOutdatedEmbeddingsAsync(CancellationToken cancellationToken = default) =>
        _indexing.MarkStaleEmbeddingsAsync(cancellationToken);

    private static IProgress<KnowledgeIndexingProgress>? Adapt(IProgress<KnowledgeIndexingProgressDto>? progress) =>
        progress is null
            ? null
            : new Progress<KnowledgeIndexingProgress>(reported => progress.Report(new KnowledgeIndexingProgressDto
            {
                FileName = reported.FileName,
                Stage = reported.Stage,
                Completed = reported.Completed,
                Total = reported.Total,
            }));

    private static KnowledgeIndexingReport WithExtraFailures(
        KnowledgeIndexingReport report,
        IReadOnlyList<KnowledgeIndexingFailure> extra) =>
        extra.Count == 0
            ? report
            : report with { Failures = [.. report.Failures, .. extra] };

    /// <summary>
    /// Checks the paths before any of them are opened, returning both the failures to report and
    /// the paths that were rejected.
    /// <para>
    /// Both are returned because the caller needs the rejected ones: handing a file that is known
    /// to be missing to the indexing service would have it discovered a second time, and reported
    /// a second time, in words meant for a different failure.
    /// </para>
    /// </summary>
    private (List<KnowledgeIndexingFailure> Failures, HashSet<string> Rejected) ValidatePaths(
        IReadOnlyList<string> filePaths)
    {
        var failures = new List<KnowledgeIndexingFailure>();
        var rejected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in filePaths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                failures.Add(new KnowledgeIndexingFailure(
                    "File",
                    ErrorCodes.DocumentNotFound,
                    "That file could not be found."));

                continue;
            }

            var name = Path.GetFileName(path);

            if (!File.Exists(path))
            {
                failures.Add(new KnowledgeIndexingFailure(
                    SafeName(name),
                    ErrorCodes.DocumentNotFound,
                    "That file could not be found."));
                rejected.Add(path);

                continue;
            }

            if (!_types.IsSupported(path))
            {
                failures.Add(new KnowledgeIndexingFailure(
                    SafeName(name),
                    ErrorCodes.DocumentFormatUnsupported,
                    "That kind of file cannot be indexed."));
                rejected.Add(path);
            }
        }

        return (failures, rejected);
    }

    private static string SafeName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "Document" : name;
}
