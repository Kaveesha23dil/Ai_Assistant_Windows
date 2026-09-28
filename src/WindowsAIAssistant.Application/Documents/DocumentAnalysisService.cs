using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Abstractions.Documents;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models;
using WindowsAIAssistant.Core.Models.Documents;

namespace WindowsAIAssistant.Application.Documents;

/// <summary>
/// Reads a document, chooses the parts of it worth sending, and asks the AI service about
/// those parts.
/// <para>
/// Two shapes of question, handled differently. A question has a small, identifiable subject,
/// so the passages that mention it are found by ranking and only those are sent. A summary has
/// no such subject, so the whole document has to be considered, and the only question is
/// whether it fits in one request: when it does not, it is summarized in parts and the parts
/// are summarized again together, rather than a document being silently described from its
/// opening pages.
/// </para>
/// <para>
/// Document text reaches the provider and nowhere else. It is not logged, not cached, and not
/// kept after a request finishes; the only thing retained from a call is the answer, which
/// belongs to the person who asked for it.
/// </para>
/// </summary>
public sealed class DocumentAnalysisService : IDocumentAnalysisService
{
    private readonly IDocumentReader _reader;
    private readonly IDocumentChunker _chunker;
    private readonly IDocumentChunkRanker _ranker;
    private readonly IAIService _ai;
    private readonly DocumentCloudConsentPolicy _consent;
    private readonly DocumentProcessingLimits _limits;
    private readonly ILogger<DocumentAnalysisService> _logger;

    public DocumentAnalysisService(
        IDocumentReader reader,
        IDocumentChunker chunker,
        IDocumentChunkRanker ranker,
        IAIService ai,
        DocumentCloudConsentPolicy consent,
        DocumentProcessingLimits limits,
        ILogger<DocumentAnalysisService> logger)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(chunker);
        ArgumentNullException.ThrowIfNull(ranker);
        ArgumentNullException.ThrowIfNull(ai);
        ArgumentNullException.ThrowIfNull(consent);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(logger);

        _reader = reader;
        _chunker = chunker;
        _ranker = ranker;
        _ai = ai;
        _consent = consent;
        _limits = limits;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DocumentAnalysisResult> AskQuestionAsync(
        DocumentAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOperation(request, DocumentAnalysisOperation.AskQuestion);
        return await CoreAsync(request, progress: null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DocumentAnalysisResult> SummarizeAsync(
        DocumentAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOperation(request, DocumentAnalysisOperation.Summarize);
        return await CoreAsync(request, progress: null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<DocumentAnalysisUpdate> StreamAnswerAsync(
        DocumentAnalysisRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A queue, rather than a callback that yields, because a yield inside a callback is not
        // a thing an async iterator can do, and because the update type is an application's
        // concern: passing the AI service's own stream type out of here would publish a Core
        // enum and a Core record from an Application namespace, and would leave callers to
        // unpack a result those types do not carry.
        var queue = Channel.CreateUnbounded<DocumentAnalysisUpdate>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        var producing = ProduceAsync();

        await foreach (var update in queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }

        await producing.ConfigureAwait(false);

        async Task ProduceAsync()
        {
            try
            {
                var result = await CoreAsync(
                    request,
                    update => queue.Writer.TryWrite(update),
                    cancellationToken).ConfigureAwait(false);

                queue.Writer.TryWrite(result.IsSuccess
                    ? DocumentAnalysisUpdate.Completed(result)
                    : DocumentAnalysisUpdate.Failed(result.ErrorCode!, result.ErrorMessage!));
            }
            catch (OperationCanceledException)
            {
                // The person stopped, or the request ran out of its time budget. Either way the
                // answer is incomplete by choice rather than by failure, and the enumerator on
                // the other end is already ending, so there is nothing to report.
            }
            catch (Exception exception)
            {
                // Whatever got this far was not one of the failures the service turns into a
                // result. It is reported the same way as any other, because an unhandled
                // exception in front of a person reading a document is the one outcome that
                // shows them a stack trace instead of a sentence.
                _logger.LogError(exception, "A streamed document request ended unexpectedly.");
                queue.Writer.TryWrite(DocumentAnalysisUpdate.Failed(
                    ErrorCodes.DocumentAnalysisFailed,
                    "The AI provider could not be reached, so this document was not analyzed."));
            }
            finally
            {
                queue.Writer.TryComplete();
            }
        }
    }

    /// <summary>
    /// Rejects a request that is not for the operation being asked for, rather than guessing.
    /// <para>
    /// A question sent to the summarize path would produce a summary and report it as an answer,
    /// which is worse than refusing: the person would be reading something the document never
    /// said and would have no reason to doubt it.
    /// </para>
    /// </summary>
    private static void EnsureOperation(DocumentAnalysisRequest request, DocumentAnalysisOperation expected)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Operation != expected)
        {
            throw new ArgumentException(
                expected == DocumentAnalysisOperation.Summarize
                    ? "This request is not a summary."
                    : "This request is not a question.",
                nameof(request));
        }
    }

    /// <summary>
    /// Reads the document, decides what of it is worth sending, and asks the AI service about
    /// that. The single implementation behind all three entry points, so streaming cannot drift
    /// from the whole-answer path.
    /// </summary>
    private async Task<DocumentAnalysisResult> CoreAsync(
        DocumentAnalysisRequest request,
        Action<DocumentAnalysisUpdate>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var fileName = request.FileName;

        try
        {
            // Read first and ask consent second. Reading a document is local work: the text is
            // extracted on this machine and goes no further, so a person whose provider is a
            // cloud one can still open a file, see its metadata, and find out that it is a scan
            // or a password-protected file. Only the step that would send it somewhere asks
            // whether it may.
            var content = await _reader.ReadAsync(request.FilePath, cancellationToken).ConfigureAwait(false);
            var chunks = await _chunker
                .ChunkAsync(content, _limits.MaximumChunksPerRequest, cancellationToken)
                .ConfigureAwait(false);

            if (chunks.Count == 0)
            {
                return DocumentAnalysisResult.Failure(
                    request.Operation,
                    fileName,
                    content.FileType,
                    ErrorCodes.DocumentEmpty,
                    request.Operation == DocumentAnalysisOperation.AskQuestion
                        ? "There was no text in this document to search."
                        : "There was no text in this document to summarize.");
            }

            _consent.EnsureAllowed();

            return request.Operation == DocumentAnalysisOperation.AskQuestion
                ? await AnswerAsync(content, chunks, request, progress, cancellationToken).ConfigureAwait(false)
                : await SummarizeAsync(content, chunks, request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DocumentException exception)
        {
            return DocumentAnalysisResult.Failure(
                request.Operation,
                fileName,
                DocumentFileType.Unknown,
                exception.ErrorCode,
                exception.Message);
        }
    }

    /// <summary>
    /// Answers a question from the passages that mention it.
    /// </summary>
    private async Task<DocumentAnalysisResult> AnswerAsync(
        DocumentContent content,
        IReadOnlyList<DocumentChunk> chunks,
        DocumentAnalysisRequest request,
        Action<DocumentAnalysisUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var ranked = _ranker.Rank(chunks, request.Question!, _limits.MaximumChunksPerRequest);
        var prompt = DocumentPromptBuilder.BuildQuestionPrompt(
            content,
            [.. ranked.Select(entry => entry.Chunk)],
            request.Question!,
            _limits.MaximumCharactersPerRequest);

        var answer = await AskAsync(prompt, progress, cancellationToken).ConfigureAwait(false);

        // Every passage that was sent is named, not only the ones that matched, because which
        // passages were considered is what lets a person judge an answer that came out of one of
        // them.
        var references = SelectReferences(content, ranked.Select(entry => entry.Chunk));

        if (answer.Contains(DocumentPromptBuilder.SourceNotFoundMarker, StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "A question about a document was not answered from the document. Passages considered: {ReferenceCount}.",
                references.Count);
        }

        return DocumentAnalysisResult.Success(
            request.Operation,
            request.FileName,
            content.FileType,
            answer,
            references,
            content.ExtractionWarnings,
            content.Metadata);
    }

    /// <summary>
    /// Summarizes a document, in one request if it fits and a part at a time if it does not.
    /// <para>
    /// Only the request whose text a person reads is streamed. The per-part summaries are work
    /// towards an answer rather than part of it, and showing them as they arrive would mean
    /// replacing text on the screen a moment later, which is worse than waiting.
    /// </para>
    /// </summary>
    private async Task<DocumentAnalysisResult> SummarizeAsync(
        DocumentContent content,
        IReadOnlyList<DocumentChunk> chunks,
        DocumentAnalysisRequest request,
        Action<DocumentAnalysisUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var (text, references) = chunks.Count == 1
            ? await SummarizeWholeAsync(content, chunks[0], request.SummaryMode, progress, cancellationToken)
                .ConfigureAwait(false)
            : await SummarizeInPartsAsync(content, chunks, request.SummaryMode, progress, cancellationToken)
                .ConfigureAwait(false);

        return DocumentAnalysisResult.Success(
            request.Operation,
            request.FileName,
            content.FileType,
            text,
            references,
            content.ExtractionWarnings,
            content.Metadata);
    }

    /// <summary>
    /// Summarizes a document that fits in one request. The whole document goes out, so the
    /// whole document is the answer's source.
    /// </summary>
    private async Task<(string Text, IReadOnlyList<string> References)> SummarizeWholeAsync(
        DocumentContent content,
        DocumentChunk chunk,
        DocumentSummaryMode mode,
        Action<DocumentAnalysisUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var prompt = DocumentPromptBuilder.BuildSummaryPrompt(
            content,
            [chunk],
            mode,
            _limits.MaximumCharactersPerRequest);

        var summary = await AskAsync(prompt, progress, cancellationToken).ConfigureAwait(false);
        return (summary, [chunk.StartReference]);
    }

    /// <summary>
    /// Summarizes a document too long for one request by summarizing it in parts and then
    /// summarizing the parts together.
    /// <para>
    /// The parts are read in document order rather than by relevance, because a summary is
    /// about the whole document and a summary of whichever parts happened to match would be a
    /// summary of the wrong thing. They are sent a few at a time: all of them at once would
    /// either exceed what a provider accepts or spend a long time waiting, and a person can
    /// wait for one answer far better than for forty in sequence.
    /// </para>
    /// </summary>
    private async Task<(string Text, IReadOnlyList<string> References)> SummarizeInPartsAsync(
        DocumentContent content,
        IReadOnlyList<DocumentChunk> chunks,
        DocumentSummaryMode mode,
        Action<DocumentAnalysisUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var partSummaries = new List<string>(chunks.Count);
        var failed = false;

        // A single request per part, with a bounded number in flight, so a large document
        // cannot open more connections than the provider will serve and then stall.
        using var throttle = new SemaphoreSlim(_limits.MaximumConcurrentChunkSummaries);

        var tasks = chunks.Select(async chunk =>
        {
            await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var partPrompt = BuildPartPrompt(content, chunk);

                // No progress sink: these are steps towards the answer, not the answer, and
                // three of them arriving at once would be three different summaries to show.
                return await AskAsync(partPrompt, progress: null, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // One part failing should not lose the other thirty-nine. The failure is counted
                // and the gap is reported, so a summary missing a chapter is not mistaken for a
                // document that did not have one.
                _logger.LogError(
                    exception,
                    "One part of a document could not be summarized. Sequence {Sequence} was skipped.",
                    chunk.Sequence);
                failed = true;
                return null;
            }
            finally
            {
                throttle.Release();
            }
        });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        foreach (var result in results)
        {
            if (!string.IsNullOrWhiteSpace(result))
            {
                partSummaries.Add(result!);
            }
        }

        if (partSummaries.Count == 0)
        {
            throw new DocumentException(
                "No part of this document could be summarized. The AI provider could not be reached.",
                ErrorCodes.DocumentAnalysisFailed);
        }

        var combined = string.Join("\n\n", partSummaries);
        var references = SelectReferences(content, chunks);

        if (partSummaries.Count < chunks.Count)
        {
            combined += "\n\n(Some sections could not be summarized and are not represented below.)";
        }

        if (combined.Length <= _limits.MaximumCharactersPerRequest)
        {
            // The parts fitted, so one more call turns the parts into a summary of the document
            // rather than a list of summaries of it.
            var finalPrompt = BuildCombinePrompt(content, combined, mode, partSummaries.Count < chunks.Count);
            var finalSummary = await AskAsync(finalPrompt, progress, cancellationToken).ConfigureAwait(false);
            return (finalSummary, references);
        }

        // Even the summaries of the parts do not fit together. Combining them again would be
        // the same problem one level up, so the parts are joined and reported as a set, and
        // the limit is named rather than exceeded.
        if (failed || partSummaries.Count < chunks.Count)
        {
            _logger.LogInformation("Part of a document was not summarized; the sections are returned separately.");
        }

        return (
            combined[..Math.Min(combined.Length, _limits.MaximumCharactersPerRequest)] +
            "\n\n(Stopped here because the document is larger than can be combined in one answer.)",
            references);
    }

    private static string BuildPartPrompt(DocumentContent content, DocumentChunk chunk) =>
        $"""
         Read the passage below and write a short factual summary of it: what it says, and any
         dates, figures, decisions, or actions it states. Do not comment on it, do not give
         advice, and do not add anything that is not in the passage. Keep it under six sentences.

         This is one passage from a longer document, so name the subject rather than assuming the
         reader knows which document it came from.

         Document: {content.Metadata.Title ?? content.Metadata.FileName}
         Passage [{chunk.StartReference}]:
         {chunk.Text}
         """;

    private static string BuildCombinePrompt(
        DocumentContent content,
        string partSummaries,
        DocumentSummaryMode mode,
        bool anyPartFailed) =>
        $"""
         Below are summaries of consecutive parts of one document, in order. Write a single
         {(mode == DocumentSummaryMode.Short ? "two or three sentence" : "cohesive")}
         summary of the whole document from them.

         Remove repetition between parts, keep the order, and do not add anything that is not
         in the part summaries. Where the parts disagree or are incomplete, say so plainly.
         {(anyPartFailed ? "Some parts are missing: say so if it matters to the summary." : string.Empty)}

         Document: {content.Metadata.Title ?? content.Metadata.FileName}

         Part summaries:
         {partSummaries}
         """;

    /// <summary>
    /// Sends one prompt and returns the text of the answer, failing with a code rather than
    /// returning a provider's error to be shown as though it were a document problem.
    /// <para>
    /// The prompt goes as a single user message through the ordinary AI service, which applies
    /// the configured model and time budget. Nothing about the request is set here that the
    /// rest of the application does not already decide for a question typed into the chat box,
    /// so a document cannot quietly be analyzed under different settings from everything else.
    /// </para>
    /// <para>
    /// With a progress sink the same request goes through the AI service's streaming call, which
    /// is the same provider under the same settings — there is no second AI service, and no
    /// second place where a document's text could be sent.
    /// </para>
    /// </summary>
    private async Task<string> AskAsync(
        string prompt,
        Action<DocumentAnalysisUpdate>? progress,
        CancellationToken cancellationToken)
    {
        return progress is null
            ? await AskWholeAsync(prompt, cancellationToken).ConfigureAwait(false)
            : await AskStreamingAsync(prompt, progress, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> AskWholeAsync(string prompt, CancellationToken cancellationToken)
    {
        AIResponse response;
        try
        {
            response = await _ai
                .SendMessageAsync([AIMessage.CreateUser(prompt)], cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The AI provider could not be reached while analyzing a document.");
            throw new DocumentException(
                "The AI provider could not be reached, so this document was not analyzed.",
                ErrorCodes.DocumentAnalysisFailed,
                exception);
        }

        return ReadAnswer(response);
    }

    private async Task<string> AskStreamingAsync(
        string prompt,
        Action<DocumentAnalysisUpdate> progress,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        AIResponse? completed = null;
        AIStreamUpdate? failure = null;

        try
        {
            var updates = _ai
                .StreamMessageAsync(AIRequest.FromMessage(prompt), cancellationToken)
                .GetAsyncEnumerator(cancellationToken);

            try
            {
                while (await updates.MoveNextAsync().ConfigureAwait(false))
                {
                    var update = updates.Current;

                    switch (update.Kind)
                    {
                        case AIStreamUpdateKind.Started:
                            progress(DocumentAnalysisUpdate.Started(
                                update.Response?.Provider ?? _ai.ActiveProvider,
                                update.Response?.Model));
                            break;

                        case AIStreamUpdateKind.Delta:
                            builder.Append(update.Text);
                            progress(DocumentAnalysisUpdate.Delta(update.Text));
                            break;

                        case AIStreamUpdateKind.Completed:
                            completed = update.Response;
                            break;

                        case AIStreamUpdateKind.Failed:
                            failure = update;
                            break;

                        default:
                            break;
                    }
                }
            }
            finally
            {
                await updates.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The AI provider could not be reached while streaming a document answer.");
            throw new DocumentException(
                "The AI provider could not be reached, so this document was not analyzed.",
                ErrorCodes.DocumentAnalysisFailed,
                exception);
        }

        if (failure is not null)
        {
            // A stream that stops halfway is reported as a failure rather than as the answer so
            // far. Half a sentence, presented under a heading that says it is the document's
            // answer, reads as though the document itself stopped mid-thought.
            _logger.LogWarning(
                "The AI provider stopped while streaming a document answer. Code: {ErrorCode}.",
                failure.ErrorCode ?? "none");

            throw new DocumentException(
                "The AI provider did not return an answer for this document. Nothing has been changed or saved.",
                ErrorCodes.DocumentAnalysisFailed);
        }

        if (completed is not null)
        {
            return ReadAnswer(completed);
        }

        // The service contract says the sequence ends with a completion or a failure. A sequence
        // that does neither is a broken provider, and is treated as one rather than as an answer
        // that happens to be short.
        _logger.LogWarning("The AI provider ended a document stream without completing or failing it.");
        throw new DocumentException(
            "The AI provider did not return an answer for this document. Nothing has been changed or saved.",
            ErrorCodes.DocumentAnalysisFailed);
    }

    /// <summary>
    /// Turns a provider's response into the text of an answer, or into a document-shaped
    /// failure. Both request shapes come through here, so a provider error cannot mean one
    /// thing when streaming and another when it does not.
    /// </summary>
    private string ReadAnswer(AIResponse? response)
    {
        if (response is null || !response.IsSuccessful || !response.HasContent)
        {
            _logger.LogWarning(
                "The AI provider returned no usable answer for a document request. Code: {ErrorCode}.",
                response?.ErrorCode ?? "none");

            throw new DocumentException(
                "The AI provider did not return an answer for this document. Nothing has been changed or saved.",
                ErrorCodes.DocumentAnalysisFailed);
        }

        return response.Content.Trim();
    }

    /// <summary>
    /// Turns the chunks actually sent into the place references shown beside an answer, one
    /// per passage, named the way the extractor named it: a page for a PDF, a slide for a
    /// presentation, a sheet for a workbook, a heading for a text file.
    /// </summary>
    private static IReadOnlyList<string> SelectReferences(
        DocumentContent content,
        IEnumerable<DocumentChunk> chunks)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var references = new List<string>();

        foreach (var chunk in chunks)
        {
            var reference = chunk.Reference;
            if (!string.IsNullOrWhiteSpace(reference) && seen.Add(reference))
            {
                references.Add(reference);
            }
        }

        return references;
    }
}
