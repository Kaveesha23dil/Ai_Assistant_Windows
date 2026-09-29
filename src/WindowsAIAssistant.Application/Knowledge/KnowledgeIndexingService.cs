using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Documents;
using WindowsAIAssistant.Core.Abstractions.Embeddings;
using WindowsAIAssistant.Core.Abstractions.Knowledge;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Abstractions.Time;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models.Documents;
using WindowsAIAssistant.Core.Models.Knowledge;

namespace WindowsAIAssistant.Application.Knowledge;

/// <summary>
/// Adds documents to a knowledge base, re-adds them when they change, and takes them out again.
/// <para>
/// Seven steps, in this order, every time: validate the path, hash the file, decide whether it
/// is new, read it, split it, embed it, write it. All seven live here so that a second caller
/// cannot assemble a version that skips the consent check or marks a document indexed before its
/// vectors exist — both of which are silent failures that would not show up until somebody asked
/// a question and got a wrong answer.
/// </para>
/// <para>
/// Files are processed one at a time. Each one opens a file, reads it, and sends a batch of
/// passages to a provider, so running several at once means several sets of those against a
/// service that rate-limits and charges per request. Adding twenty documents should take a while,
/// visibly, and report where it has got to.
/// </para>
/// <para>
/// The hash is what makes reindexing cheap and change detection honest. A passage whose text has
/// not changed keeps the vector it already has, so editing one paragraph of a long document costs
/// one embedding request rather than one per passage. And a document is recognised as the same
/// document by its content, so the same file under two names is one document and a renamed file
/// is not a new one.
/// </para>
/// <para>
/// Nothing here deletes anything. Removing a document from a base, and deleting a base, are
/// statements about the index; the files stay exactly where the person put them.
/// </para>
/// </summary>
public sealed class KnowledgeIndexingService : IKnowledgeIndexingService
{
    /// <summary>
    /// The documents currently being indexed in this process, keyed by content hash.
    /// <para>
    /// Held in memory and per process, which is enough for the case it exists for: a person
    /// pressing the button twice, or a page and a voice command both asking for the same folder
    /// at once. Two processes indexing the same file is not guarded against here, and is instead
    /// handled by the unique constraint on the hash, which the database enforces for anyone.
    /// </para>
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    private readonly IDocumentChunker _chunker;
    private readonly IDocumentReader _reader;
    private readonly IEmbeddingService _embeddings;
    private readonly IKnowledgeBaseRepository _bases;
    private readonly IKnowledgeChunkRepository _chunks;
    private readonly IKnowledgeDocumentRepository _documents;
    private readonly ILogger<KnowledgeIndexingService> _logger;
    private readonly IPermissionService _permissions;
    private readonly IDateTimeProvider _clock;
    private readonly KnowledgeProcessingLimits _limits;

    public KnowledgeIndexingService(
        IKnowledgeDocumentRepository documents,
        IKnowledgeChunkRepository chunks,
        IKnowledgeBaseRepository bases,
        IDocumentReader reader,
        IDocumentChunker chunker,
        IEmbeddingService embeddings,
        IPermissionService permissions,
        IDateTimeProvider clock,
        KnowledgeProcessingLimits limits,
        ILogger<KnowledgeIndexingService> logger)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(bases);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(chunker);
        ArgumentNullException.ThrowIfNull(embeddings);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(logger);

        _documents = documents;
        _chunks = chunks;
        _bases = bases;
        _reader = reader;
        _chunker = chunker;
        _embeddings = embeddings;
        _permissions = permissions;
        _clock = clock;
        _limits = limits;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsIndexing(string fileHash) =>
        !string.IsNullOrWhiteSpace(fileHash) && _inFlight.ContainsKey(fileHash);

    /// <inheritdoc />
    public async Task<IReadOnlyList<KnowledgeIndexingResult>> IndexAsync(
        KnowledgeIndexingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_permissions.IsGranted(PermissionCapability.KnowledgeBase))
        {
            // Refused once for the whole request rather than per file, so adding twenty documents
            // with the permission off reports one refusal instead of twenty identical ones.
            _logger.LogInformation("Indexing was refused because the knowledge base is not consented to.");
            return [KnowledgeIndexingResult.Failure(
                "Documents",
                ErrorCodes.KnowledgeOperationFailed,
                _permissions.GetDeniedMessage(PermissionCapability.KnowledgeBase))];
        }

        var results = new List<KnowledgeIndexingResult>(request.Items.Count);

        foreach (var item in request.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await IndexOneAsync(request.KnowledgeBaseId, item.FilePath, request.Progress, cancellationToken).ConfigureAwait(false));
        }

        await _bases.RefreshCountsAsync(request.KnowledgeBaseId, cancellationToken).ConfigureAwait(false);

        return results;
    }

    /// <inheritdoc />
    public async Task<KnowledgeIndexingResult> ReindexAsync(
        Guid documentId,
        IProgress<KnowledgeIndexingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!_permissions.IsGranted(PermissionCapability.KnowledgeBase))
        {
            // Checked here as well as in the add path. A reindex reads a file, splits it, and
            // embeds it, which is the same work as adding it and belongs to the same feature — so
            // a person who has turned the knowledge base off has turned off reindexing too.
            return KnowledgeIndexingResult.Failure(
                "Document",
                ErrorCodes.PermissionDenied,
                _permissions.GetDeniedMessage(PermissionCapability.KnowledgeBase));
        }

        var document = await _documents.GetAsync(documentId, cancellationToken).ConfigureAwait(false);

        if (document is null)
        {
            return KnowledgeIndexingResult.Failure(
                "Document",
                ErrorCodes.KnowledgeDocumentNotFound,
                "That document is no longer in the knowledge base.");
        }

        return await IndexOneAsync(
            document.KnowledgeBaseId,
            document.FilePath,
            progress,
            cancellationToken,
            documentId).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KnowledgeDocument>> RefreshAsync(
        Guid knowledgeBaseId,
        CancellationToken cancellationToken = default)
    {
        var documents = await _documents.ListAsync(knowledgeBaseId, cancellationToken).ConfigureAwait(false);
        var changed = new List<KnowledgeDocument>();

        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Nothing is reindexed here, deliberately. Discovering a file has changed is cheap
            // and safe; rewriting its index costs a provider request per passage and is the
            // person's decision about when to spend it.
            if (!document.Status.IsSearchable() || !File.Exists(document.FilePath))
            {
                continue;
            }

            var hash = await ComputeHashAsync(document.FilePath, cancellationToken).ConfigureAwait(false);

            if (hash is null || document.Matches(TryGetModifiedAt(document.FilePath), hash))
            {
                continue;
            }

            await _documents.MarkOutdatedAsync(document.Id, cancellationToken).ConfigureAwait(false);

            var refreshed = await _documents.GetAsync(document.Id, cancellationToken).ConfigureAwait(false);
            if (refreshed is not null)
            {
                changed.Add(refreshed);
            }
        }

        if (changed.Count > 0)
        {
            _logger.LogInformation(
                "{DocumentCount} indexed files were found to have changed on disk and were marked as outdated.",
                changed.Count);
        }

        return changed;
    }

    /// <inheritdoc />
    public Task<int> MarkStaleEmbeddingsAsync(CancellationToken cancellationToken = default)
    {
        var space = _embeddings.ActiveSpace;

        // A provider whose width is not known until a vector arrives reports no space, and then
        // nothing can be said about staleness — so nothing is marked. Marking everything on the
        // strength of an unknown space would make a person reindex their whole base for nothing.
        return space is null
            ? Task.FromResult(0)
            : _documents.MarkReindexRequiredAsync(space, cancellationToken);
    }

    /// <summary>
    /// Indexes one file: the seven steps, in order, with every outcome turned into a result.
    /// </summary>
    /// <param name="knowledgeBaseId">The base to add to.</param>
    /// <param name="filePath">The file to add.</param>
    /// <param name="progress">Where progress is reported.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <param name="existingDocumentId">
    /// The document record to update, when this is a reindex of a document already in the index.
    /// </param>
    private async Task<KnowledgeIndexingResult> IndexOneAsync(
        Guid knowledgeBaseId,
        string filePath,
        IProgress<KnowledgeIndexingProgress>? progress,
        CancellationToken cancellationToken,
        Guid? existingDocumentId = null)
    {
        var fileName = SafeFileName(filePath);

        if (!TryValidate(filePath, out var validationError))
        {
            return KnowledgeIndexingResult.Failure(fileName, ErrorCodes.ValidationError, validationError);
        }

        var fileHash = await ComputeHashAsync(filePath, cancellationToken).ConfigureAwait(false);
        if (fileHash is null)
        {
            return KnowledgeIndexingResult.Failure(
                fileName,
                ErrorCodes.DocumentNotFound,
                "That file could not be read. Check that it still exists and try again.");
        }

        if (!_inFlight.TryAdd(fileHash, 0))
        {
            return KnowledgeIndexingResult.Failure(
                fileName,
                ErrorCodes.KnowledgeDocumentIndexing,
                "That file is already being indexed.");
        }

        // Declared out here so the cancellation and failure paths can put the document back into a
        // state a person can see and act on, rather than leaving it showing as being worked on.
        var documentId = existingDocumentId ?? Guid.NewGuid();

        try
        {
            var existing = existingDocumentId is { } id
                ? await _documents.GetAsync(id, cancellationToken).ConfigureAwait(false)
                : await _documents.FindByHashAsync(fileHash, cancellationToken).ConfigureAwait(false);

            if (existing is not null && !existingDocumentId.HasValue && !existing.Status.HasNoIndexedContent())
            {
                // Already indexed, unchanged, and asked for again. Reporting success without
                // redoing the work is the right answer: the person asked for the document to be
                // in the knowledge base and it is.
                return KnowledgeIndexingResult.AlreadyIndexed(
                    existing.FileName,
                    existing.Id,
                    existing.ChunkCount);
            }

            if (existing is not null)
            {
                documentId = existing.Id;
            }

            var record = await CreateRecordAsync(
                knowledgeBaseId,
                documentId,
                filePath,
                fileHash,
                existing,
                cancellationToken).ConfigureAwait(false);

            // Saved as pending before the work starts, so a person watching a list sees the
            // document exist and be in progress rather than watching nothing happen and then
            // finding it there.
            await _documents.SaveAsync(record with { Status = KnowledgeDocumentStatus.Reading }, cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(new KnowledgeIndexingProgress(fileName, KnowledgeIndexingStage.Reading));

            DocumentContent content;
            try
            {
                content = await _reader.ReadAsync(filePath, cancellationToken).ConfigureAwait(false);
            }
            catch (DocumentException exception)
            {
                await FailAsync(documentId, exception.Message, CancellationToken.None).ConfigureAwait(false);
                return KnowledgeIndexingResult.Failure(fileName, exception.ErrorCode ?? ErrorCodes.DocumentExtractionFailed, exception.Message);
            }

            if (content.IsEmpty)
            {
                var message = "That file has no text this assistant can read. A PDF of scanned pages needs to be recognized first.";
                await FailAsync(documentId, message, cancellationToken).ConfigureAwait(false);
                return KnowledgeIndexingResult.Failure(fileName, ErrorCodes.DocumentEmpty, message);
            }

            await _documents.SetStatusAsync(documentId, KnowledgeDocumentStatus.Chunking, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(new KnowledgeIndexingProgress(fileName, KnowledgeIndexingStage.Splitting));

            var split = await _chunker
                .ChunkAsync(content, _limits.MaximumChunksPerDocument, cancellationToken)
                .ConfigureAwait(false);

            if (split.Count == 0)
            {
                var message = "That file's text could not be split into passages.";
                await FailAsync(documentId, message, cancellationToken).ConfigureAwait(false);
                return KnowledgeIndexingResult.Failure(fileName, ErrorCodes.DocumentEmpty, message);
            }

            var chunks = await BuildChunksAsync(documentId, knowledgeBaseId, fileName, split, progress, cancellationToken)
                .ConfigureAwait(false);

            if (chunks is null)
            {
                // The provider was unavailable or not permitted, so nothing was written. The
                // document is recorded as failed rather than left mid-flight, so it is not read as
                // being worked on, and it is not searchable either — an entry with no vectors would
                // contribute nothing to an answer while appearing to be indexed.
                const string message =
                    "That file's text was not embedded, so it was not added to the knowledge base. "
                    + "Check the embedding settings and start it again.";

                await FailAsync(documentId, message, CancellationToken.None).ConfigureAwait(false);

                return KnowledgeIndexingResult.Failure(
                    fileName,
                    ErrorCodes.KnowledgeEmbeddingPermissionDenied,
                    "That file's text was not sent anywhere. Turn on embeddings in Settings to index it.");
            }

            if (chunks.Count == 0)
            {
                // The file was readable but produced no passage with any text in it. Indexed as
                // zero passages it would appear in the list forever, searchable, and contribute
                // nothing to any answer — so it is reported as what it is.
                const string message = "That file's text was empty once split into passages, so it was not indexed.";

                await FailAsync(documentId, message, CancellationToken.None).ConfigureAwait(false);

                return KnowledgeIndexingResult.Failure(
                    fileName,
                    ErrorCodes.DocumentEmpty,
                    "That file had no text this assistant can index.");
            }

            progress?.Report(new KnowledgeIndexingProgress(fileName, KnowledgeIndexingStage.Saving));

            // One transaction for the document's passages, and the status becomes indexed only
            // after it commits. A search therefore never sees a document marked indexed whose
            // passages are not all there, and an interrupted reindex leaves the previous index
            // intact rather than half of each. There is deliberately no intermediate "saving"
            // status: the document stays in the embedding state it was in, which reads as
            // "not finished yet" and is true right up to the commit.
            await _chunks.ReplaceForDocumentAsync(documentId, chunks, cancellationToken).ConfigureAwait(false);

            // The space is taken from the passages themselves rather than assumed, because a
            // document whose every passage was reused from cache carries no new vector and would
            // otherwise be recorded as having no space at all — and a document with no recorded
            // space cannot be checked for compatibility later.
            var space = chunks
                .Select(chunk => chunk.Embedding?.Space)
                .FirstOrDefault(candidate => candidate is not null);

            await _documents.SaveAsync(
                record with
                {
                    Status = KnowledgeDocumentStatus.Indexed,
                    StatusMessage = null,
                    ChunkCount = chunks.Count,
                    IndexedAt = _clock.UtcNow,
                    FileSize = TryGetSize(filePath),
                    EmbeddingSpace = space,
                    FileType = content.FileType,
                },
                cancellationToken).ConfigureAwait(false);

            progress?.Report(new KnowledgeIndexingProgress(fileName, KnowledgeIndexingStage.Completed));

            return KnowledgeIndexingResult.Success(fileName, documentId, chunks.Count);
        }
        catch (OperationCanceledException)
        {
            // The person asked to stop. The document goes back to pending with a note rather than
            // being deleted, so it is visible in the list and can be started again — and it is
            // recorded with a token that is not the cancelled one, because a write made with the
            // caller's own cancelled token would be refused and the document would be left
            // showing as being worked on for the rest of the session.
            _logger.LogInformation("Indexing was stopped before the document was finished.");

            await _documents.SetStatusAsync(
                documentId,
                KnowledgeDocumentStatus.Pending,
                "Indexing was stopped before this document was finished.",
                CancellationToken.None).ConfigureAwait(false);

            return KnowledgeIndexingResult.Failure(
                fileName,
                ErrorCodes.OperationCancelled,
                "Indexing was stopped.");
        }
        catch (KnowledgeException exception)
        {
            _logger.LogWarning(
                "Indexing failed with code {ErrorCode}.",
                exception.ErrorCode);

            await FailAsync(documentId, exception.Message, CancellationToken.None).ConfigureAwait(false);

            return KnowledgeIndexingResult.Failure(
                fileName,
                exception.ErrorCode ?? ErrorCodes.KnowledgeOperationFailed,
                exception.Message);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Indexing failed unexpectedly.");

            await FailAsync(
                documentId,
                "That file could not be indexed. Try again in a moment.",
                CancellationToken.None).ConfigureAwait(false);

            return KnowledgeIndexingResult.Failure(
                fileName,
                ErrorCodes.KnowledgeOperationFailed,
                "That file could not be indexed. Try again in a moment.");
        }
        finally
        {
            _inFlight.TryRemove(fileHash, out _);
        }
    }

    /// <summary>
    /// Turns split passages into indexable chunks, embedding only the ones that need it.
    /// <para>
    /// The cache is the point. A reindex of a document edited in one place re-reads the whole
    /// file, produces mostly identical passages, and would otherwise pay to embed all of them
    /// again — for a cloud provider, the difference between reindexing a document and being
    /// billed for it. A passage whose text hash is already stored keeps the vector that hash
    /// produced.
    /// </para>
    /// <para>
    /// Returns <see langword="null"/> when the text may not be sent to a provider. That is a
    /// different outcome from a failure and the caller says so, because the remedy is a setting
    /// rather than a retry.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<KnowledgeChunk>?> BuildChunksAsync(
        Guid documentId,
        Guid knowledgeBaseId,
        string fileName,
        IReadOnlyList<DocumentChunk> split,
        IProgress<KnowledgeIndexingProgress>? progress,
        CancellationToken cancellationToken)
    {
        var pending = new List<KnowledgeChunk>(split.Count);
        var textsToEmbed = new List<string>();
        var indexesToEmbed = new List<int>();

        foreach (var chunk in split)
        {
            var text = chunk.Text.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            var contentHash = ComputeHash(text);

            pending.Add(new KnowledgeChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                KnowledgeBaseId = knowledgeBaseId,
                Sequence = pending.Count,
                Text = text,
                Sections = chunk.Sections,
                SourceReference = BuildSourceReference(chunk),
                CharacterCount = text.Length,
                ContentHash = contentHash,
            });
        }

        if (pending.Count == 0)
        {
            return [];
        }

        var cached = await _chunks
            .FindByContentHashesAsync([.. pending.Select(chunk => chunk.ContentHash)], cancellationToken)
            .ConfigureAwait(false);

        for (var index = 0; index < pending.Count; index++)
        {
            if (cached.TryGetValue(pending[index].ContentHash, out var previous) && previous.Embedding is not null)
            {
                // The cached vector is only reused when it is in the space the provider is using
                // now. A model change is exactly the case where reusing it would be wrong, and
                // this is the one place that can tell. The question is asked of the service rather
                // than by comparing spaces here, because a cloud provider's width is not known
                // until a vector arrives — and "the width is not known yet" must not be read as
                // "nothing may be reused", or every reindex of a cloud-backed document would pay
                // to embed passages that have not changed.
                if (_embeddings.IsCompatibleWith(previous.Embedding.Space))
                {
                    pending[index] = pending[index].WithEmbedding(previous.Embedding);
                }
                else
                {
                    textsToEmbed.Add(pending[index].Text);
                    indexesToEmbed.Add(index);
                }
            }
            else
            {
                textsToEmbed.Add(pending[index].Text);
                indexesToEmbed.Add(index);
            }
        }

        if (textsToEmbed.Count == 0)
        {
            return pending;
        }

        // One call for the whole document. The embedding service owns the batching — it knows the
        // provider's request size and its rate limit, and neither belongs in a caller — so this
        // reports that work has started and then that it has finished, rather than pretending to
        // a per-passage progress it cannot actually observe.
        progress?.Report(new KnowledgeIndexingProgress(
            fileName,
            KnowledgeIndexingStage.Embedding,
            0,
            textsToEmbed.Count));

        IReadOnlyList<Core.Models.Embeddings.EmbeddingVector> generated;

        try
        {
            generated = await _embeddings.GenerateBatchAsync(textsToEmbed, cancellationToken).ConfigureAwait(false);
        }
        catch (KnowledgeException exception) when (
            exception.ErrorCode is ErrorCodes.KnowledgeEmbeddingPermissionDenied
                or ErrorCodes.KnowledgeEmbeddingUnavailable
                or ErrorCodes.AiCredentialMissing)
        {
            _logger.LogInformation(
                "The passages were not embedded because the provider was unavailable or not permitted. {ErrorCode}",
                exception.ErrorCode);

            return null;
        }

        for (var index = 0; index < indexesToEmbed.Count && index < generated.Count; index++)
        {
            pending[indexesToEmbed[index]] = pending[indexesToEmbed[index]].WithEmbedding(generated[index]);
        }

        progress?.Report(new KnowledgeIndexingProgress(
            fileName,
            KnowledgeIndexingStage.Embedding,
            textsToEmbed.Count,
            textsToEmbed.Count));

        return pending;
    }

    /// <summary>
    /// Builds the record for a document being indexed, keeping the identity and the creation date
    /// of one that already exists.
    /// </summary>
    private async Task<KnowledgeDocument> CreateRecordAsync(
        Guid knowledgeBaseId,
        Guid documentId,
        string filePath,
        string fileHash,
        KnowledgeDocument? existing,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        return new KnowledgeDocument
        {
            Id = documentId,
            KnowledgeBaseId = existing?.KnowledgeBaseId ?? knowledgeBaseId,
            FileName = existing?.FileName ?? SafeFileName(filePath),
            FilePath = filePath,
            FileType = existing?.FileType ?? Core.Enums.DocumentFileType.Unknown,
            FileSize = TryGetSize(filePath),
            FileHash = fileHash,
            ModifiedAt = TryGetModifiedAt(filePath),
            IndexedAt = existing?.IndexedAt,
            ChunkCount = 0,
            Status = KnowledgeDocumentStatus.Pending,
            AddedAt = existing?.AddedAt ?? _clock.UtcNow,
        };
    }

    /// <summary>Records a failure on a document, so the list says what happened to it.</summary>
    private async Task FailAsync(Guid documentId, string message, CancellationToken cancellationToken)
    {
        try
        {
            await _documents.SetStatusAsync(
                documentId,
                KnowledgeDocumentStatus.Failed,
                message,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // The failure is already being reported to the person; failing to also record it on
            // the row is not worth masking the original for.
            _logger.LogWarning(exception, "A document's failure could not be recorded.");
        }
    }

    /// <summary>
    /// Turns a split passage's own references into the citation a person can check.
    /// <para>
    /// Only what the extractor reported, and only from a reference it recognized outright: a
    /// chunker knows a character offset and a position in the document, and an offset is not
    /// something a reader can turn to and find. Nothing here scans a section's text for a number
    /// — a heading reading "2024 review" would otherwise be cited as page 2024, which is worse
    /// than citing the file by name alone.
    /// </para>
    /// <para>
    /// A passage whose reference is one of the extractor's generic phrases, such as "the
    /// document", gets no place at all. The citation then names the file, which is true.
    /// </para>
    /// </summary>
    private static KnowledgeSourceReference BuildSourceReference(DocumentChunk chunk)
    {
        // The start of the passage only. A passage that runs from page 4 to page 6 is cited at
        // page 4: the reference names where the quoted text begins, which is the part a reader
        // can go and check, and a span would need a second field to hold the end.
        var reference = ParseReference(chunk.StartReference);

        if (reference.HasAny)
        {
            return reference;
        }

        // No reported place, so the heading the passage sat under is the most specific thing the
        // document actually said about it. A passage from a file with no headings at all gets no
        // place either, and is cited by name.
        var heading = chunk.Sections.FirstOrDefault(section => !string.IsNullOrWhiteSpace(section));

        return string.IsNullOrWhiteSpace(heading)
            ? reference
            : reference with { Heading = heading.Trim() };
    }

    /// <summary>
    /// Reads one of the extractor's own reference phrases.
    /// <para>
    /// Anchored on purpose: "page 4" is a place, "see page 4 for the appendix" is a sentence the
    /// extractor happened to write, and reading the second as the first would put a page number
    /// into a citation that nobody can check.
    /// </para>
    /// </summary>
    private static KnowledgeSourceReference ParseReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return new KnowledgeSourceReference();
        }

        var text = reference.Trim();

        if (TryReadNumber(text, "page", out var page))
        {
            return new KnowledgeSourceReference { PageNumber = page };
        }

        if (TryReadNumber(text, "slide", out var slide))
        {
            return new KnowledgeSourceReference { SlideNumber = slide };
        }

        if (text.StartsWith("Sheet \"", StringComparison.OrdinalIgnoreCase))
        {
            const string prefix = "Sheet \"";
            var end = text.IndexOf('"', prefix.Length);
            var name = end > 0 ? text[prefix.Length..end] : text[prefix.Length..].Trim();

            return string.IsNullOrWhiteSpace(name)
                ? new KnowledgeSourceReference()
                : new KnowledgeSourceReference { SheetName = name.Trim() };
        }

        // Anything else the extractor wrote is a phrase rather than a place — "the document",
        // "the whole of notes.txt" — and quoting one of those as a citation would tell a reader
        // nothing. The passage's own section names are a better answer, and the caller falls back
        // to those.
        return new KnowledgeSourceReference();
    }

    private static bool TryReadNumber(string text, string prefix, out int number)
    {
        number = 0;

        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = text[prefix.Length..].TrimStart();

        // Only the number itself may follow, so "page 4 of 9" and "page 4-5" are not read as
        // page 4 with the rest discarded.
        var digits = 0;
        while (digits < rest.Length && char.IsAsciiDigit(rest[digits]))
        {
            digits++;
        }

        if (digits == 0 || digits < rest.Length)
        {
            return false;
        }

        return int.TryParse(rest[..digits], out number) && number > 0;
    }

    private static bool TryValidate(string filePath, out string message)
    {
        message = string.Empty;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            message = "No file was selected.";
            return false;
        }

        if (!File.Exists(filePath))
        {
            message = "That file no longer exists.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// The SHA-256 of a file's bytes, in lower-case hex, or <see langword="null"/> if it could not
    /// be read.
    /// <para>
    /// The content rather than the name, the folder, or the timestamp. Any of those three is
    /// something a person can type, and all three are wrong about duplicates as soon as a file is
    /// copied, renamed, or edited and given back its old date.
    /// </para>
    /// </summary>
    private static async Task<string?> ComputeHashAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 64 * 1024,
                useAsync: true);

            var digest = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexStringLower(digest);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file locked by Word, or one the person cannot read, is an ordinary outcome and
            // the reason belongs in the result rather than in a stack trace.
            return null;
        }
    }

    private static string ComputeHash(string text) => Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes(text)));

    private static long TryGetSize(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            return info.Exists ? info.Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private static DateTimeOffset? TryGetModifiedAt(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            return info.Exists ? new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// A file's name and nothing else.
    /// <para>
    /// Used in every result, every progress report, and every log line that refers to a file. A
    /// full path in a status message would be shown in a list and written to a log, and it tells
    /// a reader where somebody keeps their files — which is not what a document list is for.
    /// </para>
    /// </summary>
    private static string SafeFileName(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return "Document";
        }

        try
        {
            var name = Path.GetFileName(filePath);
            return string.IsNullOrWhiteSpace(name) ? "Document" : name;
        }
        catch (ArgumentException)
        {
            return "Document";
        }
    }
}
