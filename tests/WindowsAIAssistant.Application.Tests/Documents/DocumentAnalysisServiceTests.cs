using Microsoft.Extensions.Logging.Abstractions;
using WindowsAIAssistant.Application.Documents;
using WindowsAIAssistant.Application.Tests.Fakes;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models.Documents;

namespace WindowsAIAssistant.Application.Tests.Documents;

/// <summary>
/// Covers the decisions the analysis service makes: which passages go to the provider, when it
/// may go at all, and what comes back.
/// <para>
/// The AI is a fake throughout. A test that reached a real provider would be slow, would cost
/// money, and would be asserting on the model's judgement rather than on this code. What is
/// worth testing is what this service decided to send and what it did with what came back, and
/// both are visible without a network.
/// </para>
/// </summary>
public sealed class DocumentAnalysisServiceTests
{
    private readonly FakeDocumentReader _reader = new();
    private readonly FakeDocumentChunker _chunker = new();
    private readonly FakeAIService _ai = new();
    private readonly FakePermissionService _permissions = new();

    [Fact]
    public async Task AShortDocumentIsSummarizedInOneRequest()
    {
        _reader.Content = FakeDocumentReader.WithText("The project deadline is October 15.");
        _chunker.WithChunk("The project deadline is October 15.");
        _ai.Response = Core.Models.AIResponse.Success("A summary of the note.", AIProviderType.OpenAI);

        var result = await Service().SummarizeAsync(DocumentAnalysisRequest.Summarize("C:/notes.txt"));

        // One chunk means one request. Splitting a document that already fits would cost a
        // second round trip and, more to the point, a second opportunity to summarize something
        // the document did not say.
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(1, _ai.CallCount);
        Assert.Equal("A summary of the note.", result.Text);
        Assert.Equal("page 1", Assert.Single(result.References));
    }

    [Fact]
    public async Task EachSummaryModeReachesTheProviderAsADifferentInstruction()
    {
        _reader.Content = FakeDocumentReader.WithText("Some content about a deadline.");
        _chunker.WithChunk("Some content about a deadline.");

        foreach (var mode in new[] { DocumentSummaryMode.Short, DocumentSummaryMode.Standard, DocumentSummaryMode.Detailed })
        {
            await Service().SummarizeAsync(DocumentAnalysisRequest.Summarize("C:/notes.txt", mode));
        }

        var prompts = Enumerable.Range(0, _ai.CallCount)
            .Select(_ => _ai.LastMessage?.Content ?? string.Empty)
            .ToArray();

        Assert.All(prompts, prompt => Assert.Contains("Summarize", prompt, StringComparison.Ordinal));

        // The mode has to reach the model as a different instruction, not as a label it ignores.
        Assert.Contains(prompts, prompt => prompt.Contains("short", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(prompts, prompt => prompt.Contains("detailed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ALongDocumentIsSummarizedInPartsAndThenAsAWhole()
    {
        for (var index = 0; index < 3; index++)
        {
            _reader.Content = FakeDocumentReader.WithSections(
                $"Part one text {index}.", $"Part two text {index}.", $"Part three text {index}.");
            _chunker.WithChunk($"Part {index} text.", index, $"page {index + 1}");
        }

        _ai.Response = Core.Models.AIResponse.Success("A part summary.", AIProviderType.OpenAI);

        var result = await Service().SummarizeAsync(DocumentAnalysisRequest.Summarize("C:/report.pdf"));

        // Three parts, then one request to turn the parts into a summary of the document. What
        // must not happen is a single request carrying all three parts as though the whole
        // document were small enough, which is how a long report gets described from its
        // opening pages.
        Assert.Equal(4, _ai.CallCount);
        Assert.Equal(3, result.References.Count);
    }

    [Fact]
    public async Task AQuestionIsAnsweredFromThePassageThatMentionsItFirst()
    {
        _reader.Content = FakeDocumentReader.WithSections(
            "The office moved to Bridge Street in June.",
            "The project deadline is October 15.",
            "Parking is arranged through the building manager.");

        _chunker
            .WithChunk("The office moved to Bridge Street in June.", 0, "page 1")
            .WithChunk("The project deadline is October 15.", 1, "page 2")
            .WithChunk("Parking is arranged through the building manager.", 2, "page 3");

        _ai.Response = Core.Models.AIResponse.Success("The deadline is October 15.", AIProviderType.OpenAI);

        var result = await Service().AskQuestionAsync(
            DocumentAnalysisRequest.AskQuestion("C:/report.pdf", "When is the project deadline?"));

        var prompt = _ai.LastMessage?.Content ?? string.Empty;

        Assert.Equal("The deadline is October 15.", result.Text);

        // A short document is sent whole, which is right: the cost of sending all of it is less
        // than the cost of guessing which part of it matters. What ranking still decides is the
        // order, so that when a context is cut short it is the end that goes.
        Assert.Contains("October 15", prompt, StringComparison.Ordinal);
        Assert.True(
            prompt.IndexOf("[page 2]", StringComparison.Ordinal) <
            prompt.IndexOf("[page 3]", StringComparison.Ordinal),
            "the passage holding the answer was not sent first");

        // And the answer says which page it came from, so a reader can go and check.
        Assert.Contains("page 2", result.References);
    }

    [Fact]
    public async Task AQuestionAboutALongDocumentReadsOnlyTheBoundedPartAndLeadsWithTheAnswer()
    {
        // The case the bounds exist for. A document with more passages in it than one request can
        // carry is read up to the limit, and the passage holding the answer is what leads.
        for (var index = 0; index < 30; index++)
        {
            _chunker.WithChunk(
                index == 3 ? "The project deadline is October 15." : $"Unrelated page {index} about parking.",
                index,
                $"page {index + 1}");
        }

        _reader.Content = FakeDocumentReader.WithSections("placeholder");
        _ai.Provider = AIProviderType.Local;
        _ai.Response = Core.Models.AIResponse.Success("The deadline is October 15.", AIProviderType.Local);

        var result = await Service(limits: new DocumentProcessingLimits { MaximumChunksPerRequest = 5 })
            .AskQuestionAsync(DocumentAnalysisRequest.AskQuestion("C:/long.pdf", "When is the project deadline?"));

        var prompt = _ai.LastMessage?.Content ?? string.Empty;

        Assert.True(result.IsSuccess, result.ErrorMessage);

        // The passage with the answer in it, and only five passages out of thirty.
        Assert.Contains("October 15", prompt, StringComparison.Ordinal);
        Assert.Contains("[page 4]", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("[page 9]", prompt, StringComparison.Ordinal);
        Assert.Equal(5, CountOccurrences(prompt, "[page "));
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    [Fact]
    public async Task AQuestionPromptInstructsTheModelToRefuseRatherThanFillTheGap()
    {
        _reader.Content = FakeDocumentReader.WithText("The project deadline is October 15.");
        _chunker.WithChunk("The project deadline is October 15.");
        _ai.Response = Core.Models.AIResponse.Success("An answer.", AIProviderType.OpenAI);

        await Service().AskQuestionAsync(
            DocumentAnalysisRequest.AskQuestion("C:/notes.txt", "When is the CEO's birthday?"));

        var prompt = _ai.LastMessage?.Content ?? string.Empty;

        // This is the instruction that decides whether a document answer is a grounded answer or
        // a confident guess, so its presence is asserted rather than its wording trusted.
        Assert.Contains(DocumentPromptBuilder.SourceNotFoundMarker, prompt, StringComparison.Ordinal);
        Assert.Contains("only from the document", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CEO's birthday", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAnswerTheDocumentDoesNotContainIsReportedAsUnanswered()
    {
        _reader.Content = FakeDocumentReader.WithText("The project deadline is October 15.");
        _chunker.WithChunk("The project deadline is October 15.");

        // What a model replies when the passages do not contain the answer.
        _ai.Response = Core.Models.AIResponse.Success(
            DocumentPromptBuilder.SourceNotFoundMarker,
            AIProviderType.OpenAI);

        var result = await Service().AskQuestionAsync(
            DocumentAnalysisRequest.AskQuestion("C:/notes.txt", "When is the CEO's birthday?"));

        // A different outcome from a failure, and shown differently: the document was read
        // perfectly well, it just does not say.
        Assert.True(result.IsSuccess);
        Assert.Equal(DocumentPromptBuilder.SourceNotFoundMarker, result.Text);
    }

    [Fact]
    public async Task CloudDocumentProcessingBeingOffStopsTheSummaryAfterTheFileIsRead()
    {
        // The case this exists for: a remote provider selected, and documents not permitted to
        // go to it. Reading the file is local work and is still allowed, so a person can see that
        // the document is real and readable; the request for a summary is refused.
        _reader.Content = FakeDocumentReader.WithText("The project deadline is October 15.");
        _chunker.WithChunk("The project deadline is October 15.");
        _ai.Provider = AIProviderType.OpenAI;
        _ai.Response = Core.Models.AIResponse.Success("A summary.", AIProviderType.OpenAI);
        _permissions.Denied.Add(PermissionCapability.DocumentCloudProcessing);

        var result = await Service().SummarizeAsync(DocumentAnalysisRequest.Summarize("C:/notes.txt"));

        Assert.Equal(ErrorCodes.DocumentAiPermissionDenied, result.ErrorCode);

        // Nothing left the machine, which is the whole claim being made.
        Assert.Equal(0, _ai.CallCount);

        // And the file was still read, so extraction is unaffected by a cloud setting.
        Assert.Equal(1, _reader.ReadCount);
    }

    [Fact]
    public async Task CloudAiBeingOffAlsoStopsTheSummary()
    {
        _reader.Content = FakeDocumentReader.WithText("Content.");
        _chunker.WithChunk("Content.");
        _ai.Provider = AIProviderType.OpenAI;
        _permissions.Denied.Add(PermissionCapability.CloudAI);

        var result = await Service().SummarizeAsync(DocumentAnalysisRequest.Summarize("C:/notes.txt"));

        Assert.Equal(ErrorCodes.DocumentAiPermissionDenied, result.ErrorCode);
        Assert.Equal(0, _ai.CallCount);
    }

    [Fact]
    public async Task AQuestionIsAlsoStoppedWhenDocumentsMayNotBeSentToTheCloud()
    {
        _reader.Content = FakeDocumentReader.WithText("Content.");
        _chunker.WithChunk("Content.");
        _ai.Provider = AIProviderType.AzureOpenAI;
        _permissions.Denied.Add(PermissionCapability.DocumentCloudProcessing);

        var result = await Service().AskQuestionAsync(
            DocumentAnalysisRequest.AskQuestion("C:/notes.txt", "What does it say?"));

        Assert.Equal(ErrorCodes.DocumentAiPermissionDenied, result.ErrorCode);
        Assert.Equal(0, _ai.CallCount);
    }

    [Fact]
    public async Task AProviderOnThisComputerNeedsNoCloudPermission()
    {
        // The other half of the same rule. A local provider is not a remote one, so the text
        // does not leave the machine and there is nothing to consent to.
        _reader.Content = FakeDocumentReader.WithText("The project deadline is October 15.");
        _chunker.WithChunk("The project deadline is October 15.");
        _ai.Provider = AIProviderType.Local;
        _ai.Response = Core.Models.AIResponse.Success("The deadline is October 15.", AIProviderType.Local);
        _permissions.Denied.Add(PermissionCapability.CloudAI);
        _permissions.Denied.Add(PermissionCapability.DocumentCloudProcessing);

        var result = await Service().AskQuestionAsync(
            DocumentAnalysisRequest.AskQuestion("C:/notes.txt", "When is the deadline?"));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(1, _ai.CallCount);
    }

    [Fact]
    public async Task AnUnrecognizedProviderIsTreatedAsRemote()
    {
        // A provider this build does not know about has not been shown to be local, so it is
        // asked about rather than trusted with a document.
        _reader.Content = FakeDocumentReader.WithText("Content.");
        _chunker.WithChunk("Content.");
        _ai.Provider = AIProviderType.Custom;
        _permissions.Denied.Add(PermissionCapability.DocumentCloudProcessing);

        var result = await Service().SummarizeAsync(DocumentAnalysisRequest.Summarize("C:/notes.txt"));

        Assert.Equal(ErrorCodes.DocumentAiPermissionDenied, result.ErrorCode);
        Assert.Equal(0, _ai.CallCount);
    }

    [Fact]
    public async Task AFileThatCannotBeReadIsReportedWithItsOwnReason()
    {
        _reader.ExceptionToThrow = new DocumentException(
            "This PDF appears to contain scanned pages or images. OCR is not enabled yet.",
            ErrorCodes.DocumentOcrRequired);

        var result = await Service().SummarizeAsync(DocumentAnalysisRequest.Summarize("C:/scan.pdf"));

        // A document that needs OCR is a different problem from a permission that is off, and the
        // person needs to be told which one they have.
        Assert.Equal(ErrorCodes.DocumentOcrRequired, result.ErrorCode);
        Assert.Contains("scanned", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ADocumentWithNoTextIsReportedRatherThanSummarizedAsNothing()
    {
        _reader.ExceptionToThrow = new DocumentException(
            "No readable text was found in notes.txt.",
            ErrorCodes.DocumentEmpty);

        var result = await Service().SummarizeAsync(DocumentAnalysisRequest.Summarize("C:/notes.txt"));

        Assert.Equal(ErrorCodes.DocumentEmpty, result.ErrorCode);
        Assert.Equal(0, _ai.CallCount);
    }

    [Fact]
    public async Task AnUnreachableProviderIsReportedAsAFailureRatherThanAsADocumentProblem()
    {
        _reader.Content = FakeDocumentReader.WithText("Content.");
        _chunker.WithChunk("Content.");
        _ai.Provider = AIProviderType.Local;
        _ai.ExceptionToThrow = new HttpRequestException("The provider could not be reached.");

        var result = await Service().SummarizeAsync(DocumentAnalysisRequest.Summarize("C:/notes.txt"));

        // A provider being down is not the document's fault, and saying "this document could not
        // be analyzed" when the file is fine would send a person looking in the wrong place.
        Assert.Equal(ErrorCodes.DocumentAnalysisFailed, result.ErrorCode);
    }

    [Fact]
    public async Task AnEmptyAnswerFromTheProviderIsNotReturnedAsASummary()
    {
        _reader.Content = FakeDocumentReader.WithText("Content.");
        _chunker.WithChunk("Content.");
        _ai.Provider = AIProviderType.Local;
        _ai.Response = Core.Models.AIResponse.Failure("Rate limited.", AIProviderType.Local, "RATE_LIMITED");

        var result = await Service().SummarizeAsync(DocumentAnalysisRequest.Summarize("C:/notes.txt"));

        Assert.Equal(ErrorCodes.DocumentAnalysisFailed, result.ErrorCode);
    }

    [Fact]
    public async Task WarningsMetWhileReadingAreCarriedOntoTheAnswer()
    {
        _reader.Content = FakeDocumentReader.WithText(
            "Half a document.",
            "partial.pdf",
            DocumentFileType.Pdf,
            [DocumentWarningMessages.Create(DocumentWarningKind.SpreadsheetTruncated)]);
        _chunker.WithChunk("Half a document.");
        _ai.Provider = AIProviderType.Local;

        var result = await Service().SummarizeAsync(DocumentAnalysisRequest.Summarize("C:/partial.pdf"));

        // A summary of part of a document has to say it was a summary of part of a document.
        Assert.Contains(result.Warnings, warning => warning.Kind == DocumentWarningKind.SpreadsheetTruncated);
    }

    [Fact]
    public async Task ACancelledRequestStopsRatherThanReturningAPartialAnswer()
    {
        _reader.Content = FakeDocumentReader.WithText(string.Join(' ', Enumerable.Repeat("content", 5_000)));
        _chunker.WithChunk("content");

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Service().SummarizeAsync(DocumentAnalysisRequest.Summarize("C:/notes.txt"), cancellation.Token));

        Assert.Equal(0, _ai.CallCount);
    }

    [Fact]
    public async Task ARequestForTheWrongOperationIsRejectedRatherThanGuessedAt()
    {
        var request = DocumentAnalysisRequest.AskQuestion("C:/notes.txt", "What does it say?");

        await Assert.ThrowsAsync<ArgumentException>(() => Service().SummarizeAsync(request));
    }

    [Fact]
    public async Task ThePathIsNeverSentToTheProvider()
    {
        // The document's name is in the prompt so the model can refer to it. Its location is not,
        // because a model has no use for where somebody keeps their files and it is the one part
        // of a document that would be a privacy cost rather than a benefit.
        _reader.Content = FakeDocumentReader.WithText("Content.", "private.txt");
        _chunker.WithChunk("Content.");
        _ai.Provider = AIProviderType.Local;

        await Service().AskQuestionAsync(
            DocumentAnalysisRequest.AskQuestion("C:/Users/someone/Documents/private.txt", "What does it say?"));

        var prompt = _ai.LastMessage?.Content ?? string.Empty;

        Assert.DoesNotContain("C:/Users", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Documents", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("private.txt", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDocumentTextReachesTheProviderAndNothingElseDoes()
    {
        _reader.Content = FakeDocumentReader.WithText("The project deadline is October 15.");
        _chunker.WithChunk("The project deadline is October 15.");
        _ai.Provider = AIProviderType.Local;

        await Service().AskQuestionAsync(
            DocumentAnalysisRequest.AskQuestion("C:/notes.txt", "When is the deadline?"));

        var prompt = _ai.LastMessage?.Content ?? string.Empty;

        // The text has to actually be in the prompt, or a grounded answer is a guess.
        Assert.Contains("The project deadline is October 15.", prompt, StringComparison.Ordinal);
    }

    private DocumentAnalysisService Service(DocumentProcessingLimits? limits = null) =>
        new(
            _reader,
            _chunker,
            new DocumentChunkRanker(NullLogger<DocumentChunkRanker>.Instance),
            _ai,
            new DocumentCloudConsentPolicy(_permissions, _ai, NullLogger<DocumentCloudConsentPolicy>.Instance),
            limits ?? new DocumentProcessingLimits(),
            NullLogger<DocumentAnalysisService>.Instance);
}
