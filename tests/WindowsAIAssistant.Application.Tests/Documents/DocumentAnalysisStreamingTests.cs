using Microsoft.Extensions.Logging.Abstractions;
using WindowsAIAssistant.Application.Documents;
using WindowsAIAssistant.Application.Tests.Fakes;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;
using WindowsAIAssistant.Core.Models.Documents;

namespace WindowsAIAssistant.Application.Tests.Documents;

/// <summary>
/// Covers the streamed answer about a document: that the text arrives as it is produced, that
/// the finished update carries the places the answer came from, and that a stream which stops
/// halfway is reported as a stop rather than as an answer.
/// </summary>
public sealed class DocumentAnalysisStreamingTests
{
    private readonly FakeDocumentReader _reader = new();
    private readonly FakeDocumentChunker _chunker = new();
    private readonly FakeAIService _ai = new();
    private readonly FakePermissionService _permissions = new();

    [Fact]
    public async Task AQuestionIsStreamedAsItArrivesAndEndsWithThePlacesItCameFrom()
    {
        _reader.Content = FakeDocumentReader.WithSections("The project deadline is October 15.");
        _chunker.WithChunk("The project deadline is October 15.");
        _ai.Provider = AIProviderType.Local;
        _ai.Response = AIResponse.Success(
            "The deadline is October 15, and the client will present the final plan the week before.",
            AIProviderType.Local);

        var updates = await CollectAsync(
            Service().StreamAnswerAsync(DocumentAnalysisRequest.AskQuestion("C:/notes.txt", "When is the deadline?")));

        // The fragments really are fragments, not one answer delivered twice: a page that only
        // ever received a single update at the end would look identical to this one in a test
        // that did not count, and a person would still be waiting through the whole answer.
        var deltas = updates.Where(update => update.Kind == DocumentAnalysisUpdateKind.Delta).ToArray();
        Assert.True(deltas.Length > 1, "the answer arrived in one piece rather than as a stream");
        Assert.Equal("The deadline is October 15, and the client will present the final plan the week before.",
            string.Concat(deltas.Select(delta => delta.Text)).Trim());

        // One start, and exactly one ending, so a caller never has to guess whether the answer
        // is finished.
        Assert.Equal(DocumentAnalysisUpdateKind.Started, updates[0].Kind);
        Assert.Equal(AIProviderType.Local, updates[0].Provider);
        Assert.Equal(DocumentAnalysisUpdateKind.Completed, updates[^1].Kind);

        // The references are known only at the end, and the person needs them: an answer with no
        // page behind it cannot be checked against the document.
        var result = Assert.IsType<DocumentAnalysisResult>(updates[^1].Result);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("page 1", Assert.Single(result.References));
    }

    [Fact]
    public async Task AStreamedAnswerAndAWholeAnswerProduceTheSameText()
    {
        _reader.Content = FakeDocumentReader.WithSections("The project deadline is October 15.");
        _chunker.WithChunk("The project deadline is October 15.");
        _ai.Provider = AIProviderType.Local;
        _ai.Response = AIResponse.Success("The deadline is October 15.", AIProviderType.Local);

        var streamed = await CollectAsync(
            Service().StreamAnswerAsync(DocumentAnalysisRequest.AskQuestion("C:/notes.txt", "When?")));

        var whole = await Service().AskQuestionAsync(
            DocumentAnalysisRequest.AskQuestion("C:/notes.txt", "When?"));

        // Streaming is a different way of showing the same work, not a different answer. A
        // divergence here would mean the two entry points have stopped agreeing.
        Assert.Equal(whole.Text, streamed[^1].Result!.Text);
        Assert.Equal(whole.References, streamed[^1].Result!.References);
    }

    [Fact]
    public async Task AStreamThatStopsHalfwayIsReportedAsAFailureRatherThanAsAnAnswer()
    {
        _reader.Content = FakeDocumentReader.WithSections("Content.");
        _chunker.WithChunk("Content.");
        _ai.Provider = AIProviderType.Local;
        _ai.StreamUpdates =
        [
            AIStreamUpdate.Started(AIProviderType.Local, "fake-model"),
            AIStreamUpdate.Delta("The deadline is October"),
            AIStreamUpdate.Failed(
                "The deadline is October",
                AIResponse.Failure("The connection closed.", AIProviderType.Local, "CONNECTION_CLOSED"),
                "CONNECTION_CLOSED",
                "The connection closed."),
        ];

        var updates = await CollectAsync(
            Service().StreamAnswerAsync(DocumentAnalysisRequest.AskQuestion("C:/notes.txt", "When?")));

        Assert.Equal(DocumentAnalysisUpdateKind.Failed, updates[^1].Kind);
        Assert.Equal(ErrorCodes.DocumentAnalysisFailed, updates[^1].ErrorCode);

        // Not completed: half a sentence about a deadline is not a document's answer, and
        // presenting it as one would put a claim on screen that the document never made.
        Assert.DoesNotContain(updates, update => update.Kind == DocumentAnalysisUpdateKind.Completed);
    }

    [Fact]
    public async Task AStreamThatEndsWithNeitherCompletionNorFailureIsAFailure()
    {
        _reader.Content = FakeDocumentReader.WithSections("Content.");
        _chunker.WithChunk("Content.");
        _ai.Provider = AIProviderType.Local;
        _ai.StreamUpdates = [AIStreamUpdate.Delta("Half an answer with no ending.")];

        var updates = await CollectAsync(
            Service().StreamAnswerAsync(DocumentAnalysisRequest.AskQuestion("C:/notes.txt", "When?")));

        // A provider that breaks its contract must not be taken at its word. Treating silence
        // after a partial answer as success would return a truncated sentence as the document's
        // answer, and the person asking would have no way to tell.
        Assert.Equal(DocumentAnalysisUpdateKind.Failed, updates[^1].Kind);
        Assert.Equal(ErrorCodes.DocumentAnalysisFailed, updates[^1].ErrorCode);
    }

    [Fact]
    public async Task AProviderThatThrowsMidStreamIsReportedAsAFailure()
    {
        _reader.Content = FakeDocumentReader.WithSections("Content.");
        _chunker.WithChunk("Content.");
        _ai.Provider = AIProviderType.Local;
        _ai.StreamExceptionToThrow = new HttpRequestException("The connection was reset.");

        var updates = await CollectAsync(
            Service().StreamAnswerAsync(DocumentAnalysisRequest.AskQuestion("C:/notes.txt", "When?")));

        Assert.Equal(DocumentAnalysisUpdateKind.Failed, updates[^1].Kind);
        Assert.Equal(ErrorCodes.DocumentAnalysisFailed, updates[^1].ErrorCode);
    }

    [Fact]
    public async Task CloudPermissionIsStillEnforcedOnTheStreamedPath()
    {
        // The same refusal as the whole-answer path, and the reason it matters here: streaming
        // sends the document's text in several pieces rather than one, so a check that lived
        // only on one of the two paths would be a check that could be avoided.
        _reader.Content = FakeDocumentReader.WithText("Content.");
        _chunker.WithChunk("Content.");
        _ai.Provider = AIProviderType.OpenAI;
        _permissions.Denied.Add(PermissionCapability.DocumentCloudProcessing);

        var updates = await CollectAsync(
            Service().StreamAnswerAsync(DocumentAnalysisRequest.AskQuestion("C:/notes.txt", "What does it say?")));

        Assert.Equal(DocumentAnalysisUpdateKind.Failed, updates[^1].Kind);
        Assert.Equal(ErrorCodes.DocumentAiPermissionDenied, updates[^1].ErrorCode);
        Assert.Equal(0, _ai.CallCount);
    }

    [Fact]
    public async Task ADocumentThatCannotBeReadEndsTheStreamWithItsOwnReason()
    {
        _reader.ExceptionToThrow = new Core.Exceptions.DocumentException(
            "This PDF appears to contain scanned pages or images. OCR is not enabled yet.",
            ErrorCodes.DocumentOcrRequired);

        var updates = await CollectAsync(
            Service().StreamAnswerAsync(DocumentAnalysisRequest.Summarize("C:/scan.pdf")));

        Assert.Equal(DocumentAnalysisUpdateKind.Failed, updates[^1].Kind);
        Assert.Equal(ErrorCodes.DocumentOcrRequired, updates[^1].ErrorCode);
    }

    [Fact]
    public async Task AStreamedSummaryShowsOnlyTheFinalSummaryAndNotThePartsBehindIt()
    {
        for (var index = 0; index < 3; index++)
        {
            _chunker.WithChunk($"Part {index} text.", index, $"page {index + 1}");
        }

        _reader.Content = FakeDocumentReader.WithSections("placeholder");
        _ai.Provider = AIProviderType.Local;
        _ai.Response = AIResponse.Success("A summary of the whole document.", AIProviderType.Local);

        var updates = await CollectAsync(
            Service().StreamAnswerAsync(DocumentAnalysisRequest.Summarize("C:/long.pdf")));

        var fragments = string.Concat(updates
            .Where(update => update.Kind == DocumentAnalysisUpdateKind.Delta)
            .Select(update => update.Text));

        // The parts were summarized on the way, and none of them belongs on the screen: the text
        // a person reads is the summary of all of it, and showing the steps would mean
        // replacing what they are reading a moment later.
        Assert.Equal("A summary of the whole document.", fragments.Trim());
        Assert.Equal(DocumentAnalysisUpdateKind.Completed, updates[^1].Kind);
        Assert.Equal(3, updates[^1].Result!.References.Count);
    }

    [Fact]
    public async Task AStoppedStreamIsCancellationRatherThanAFailure()
    {
        _reader.Content = FakeDocumentReader.WithSections("Content.");
        _chunker.WithChunk("Content.");
        _ai.Provider = AIProviderType.Local;
        _ai.Response = AIResponse.Success("A long answer.", AIProviderType.Local);
        _ai.FragmentDelay = TimeSpan.FromMilliseconds(20);

        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var update in Service()
                .StreamAnswerAsync(DocumentAnalysisRequest.AskQuestion("C:/notes.txt", "When?"), cancellation.Token)
                .WithCancellation(cancellation.Token))
            {
                // Stop partway, the way the Stop button does.
                await cancellation.CancelAsync();
            }
        });
    }

    private static async Task<List<DocumentAnalysisUpdate>> CollectAsync(IAsyncEnumerable<DocumentAnalysisUpdate> updates)
    {
        var collected = new List<DocumentAnalysisUpdate>();
        await foreach (var update in updates)
        {
            collected.Add(update);
        }

        return collected;
    }

    private DocumentAnalysisService Service() =>
        new(
            _reader,
            _chunker,
            new DocumentChunkRanker(NullLogger<DocumentChunkRanker>.Instance),
            _ai,
            new DocumentCloudConsentPolicy(_permissions, _ai, NullLogger<DocumentCloudConsentPolicy>.Instance),
            new DocumentProcessingLimits(),
            NullLogger<DocumentAnalysisService>.Instance);
}
