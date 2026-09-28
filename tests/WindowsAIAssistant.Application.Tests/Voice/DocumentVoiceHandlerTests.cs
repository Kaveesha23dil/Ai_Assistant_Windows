using Microsoft.Extensions.Logging.Abstractions;
using WindowsAIAssistant.Application.Documents.Queries.AskDocumentQuestion;
using WindowsAIAssistant.Application.Documents.Queries.SummarizeDocument;
using WindowsAIAssistant.Application.Tests.Fakes;
using WindowsAIAssistant.Application.Voice.Commands.Document;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Tests.Voice;

/// <summary>
/// Covers what happens when a document is asked for out loud rather than typed.
/// <para>
/// Voice is the one place where a document's text is answered without anybody watching the
/// screen, which makes three things worth proving. That the answer is short enough to be worth
/// listening to. That a refusal to send the document to a provider is spoken as a refusal
/// rather than as a failure of the document. And that the full text is left on the page instead
/// of being read out in full, because a paragraph nobody can act on is worse than no answer.
/// </para>
/// </summary>
public sealed class DocumentVoiceHandlerTests
{
    [Fact]
    public async Task AskingToOpenADocumentMovesToTheDocumentPageWithoutConsent()
    {
        var (handler, navigator, _) = Handler();

        var result = await handler.ExecuteAsync(Command(
            AssistantIntent.OpenDocument,
            ("file", @"C:\Notes\report.pdf")));

        Assert.True(result.IsSuccess);
        Assert.Equal(NavigationRoute.Document, Assert.Single(navigator.Requests));

        // Opening a page reads nothing, so it is not gated. A person who cannot open their own
        // file because a cloud provider is switched off would be a defect, not a safeguard.
        Assert.False(result.IsPermissionDenied);
    }

    [Fact]
    public async Task ASpokenSummarySaysWhereTheFullTextIsRatherThanReadingItOut()
    {
        var (handler, _, _) = Handler(answer: "A long and detailed summary that would be tedious aloud.");

        var result = await handler.ExecuteAsync(Command(
            AssistantIntent.SummarizeDocument,
            ("file", @"C:\Notes\report.pdf")));

        Assert.True(result.IsSuccess);

        // The document's name is worth saying; the summary is not, because it is on the page and
        // a person listening to it cannot act until they can see it anyway.
        Assert.Contains("report.pdf", result.ResponseText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("document page", result.ResponseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tedious aloud", result.ResponseText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ASpokenQuestionIsAnsweredOutLoudWhenTheDocumentAnswersIt()
    {
        var (handler, _, _) = Handler(answer: "The deadline is October 15.");

        var result = await handler.ExecuteAsync(Command(
            AssistantIntent.AskDocumentQuestion,
            ("file", @"C:\Notes\report.pdf"),
            ("query", "When is the deadline?")));

        Assert.True(result.IsSuccess);
        Assert.Equal("The deadline is October 15.", result.ResponseText);
        Assert.Equal("true", result.Data["answered"]);
    }

    [Fact]
    public async Task AQuestionTheDocumentDoesNotAnswerIsSaidToBeUnanswered()
    {
        // What a model replies when the passages sent do not contain the answer: the marker the
        // prompt tells it to use, and nothing else.
        var (handler, _, _) = Handler(answer: WindowsAIAssistant.Application.Documents.DocumentPromptBuilder.SourceNotFoundMarker);

        var result = await handler.ExecuteAsync(Command(
            AssistantIntent.AskDocumentQuestion,
            ("file", @"C:\Notes\report.pdf"),
            ("query", "When is the CEO's birthday?")));

        // A different outcome from a failure, and a person hearing it needs to be able to tell
        // the difference: the document is fine, it just does not say.
        Assert.True(result.IsSuccess);
        Assert.Equal("false", result.Data["answered"]);
    }

    [Fact]
    public async Task ALongAnswerIsTrimmedToSomethingWorthListeningTo()
    {
        var long_ = new string('a', 2_000) + ". And then some more.";
        var (handler, _, _) = Handler(answer: long_);

        var result = await handler.ExecuteAsync(Command(
            AssistantIntent.AskDocumentQuestion,
            ("file", @"C:\Notes\report.pdf"),
            ("query", "What does it say?")));

        Assert.True(result.IsSuccess);

        // Trimmed, and honest about having been: an answer that stops without saying so sounds
        // like the document ended there.
        Assert.True(result.ResponseText.Length < long_.Length);
        Assert.Contains("full answer is on the document page", result.ResponseText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ARefusalToSendTheDocumentIsSpokenAsARefusalAndNotAsABrokenDocument()
    {
        var (handler, _, _) = Handler(
            provider: AIProviderType.OpenAI,
            deny: PermissionCapability.DocumentCloudProcessing);

        var result = await handler.ExecuteAsync(Command(
            AssistantIntent.SummarizeDocument,
            ("file", @"C:\Notes\report.pdf")));

        // Reported as a permission failure with the code the consent policy used, so the caller
        // can distinguish "you have not allowed this" from "this file is unreadable". Getting
        // this wrong would send a person looking for a broken document that is perfectly fine.
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.DocumentAiPermissionDenied, result.ErrorCode);
        Assert.True(result.IsPermissionDenied);
    }

    [Fact]
    public async Task AProviderOnThisComputerIsAskedNormally()
    {
        var (handler, _, _) = Handler(
            provider: AIProviderType.Local,
            deny: PermissionCapability.DocumentCloudProcessing);

        var result = await handler.ExecuteAsync(Command(
            AssistantIntent.SummarizeDocument,
            ("file", @"C:\Notes\report.pdf")));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ALimitationMetWhileReadingIsSpokenAsWell()
    {
        var (handler, _, _) = Handler(
            warnings: [Core.Models.Documents.DocumentWarningMessages.Create(
                DocumentWarningKind.SpreadsheetTruncated)]);

        var result = await handler.ExecuteAsync(Command(
            AssistantIntent.SummarizeDocument,
            ("file", @"C:\Notes\book.xlsx")));

        // A summary of a truncated spreadsheet is not the same as a summary of the workbook, and
        // the person has to be told which one they are being given.
        Assert.True(result.IsSuccess);
        Assert.Contains("book.xlsx", result.ResponseText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ADocumentThatCannotBeReadIsReportedAsSuch()
    {
        var (handler, _, _) = Handler(
            readFailure: new Core.Exceptions.DocumentException(
                "This PDF appears to contain scanned pages or images. OCR is not enabled yet.",
                ErrorCodes.DocumentOcrRequired));

        var result = await handler.ExecuteAsync(Command(
            AssistantIntent.SummarizeDocument,
            ("file", @"C:\Notes\scan.pdf")));

        Assert.False(result.IsSuccess);
        Assert.Contains("scanned", result.ResponseText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ASummaryOrQuestionWithNoFileNamedIsRejectedBeforeAnythingIsRead()
    {
        var (handler, navigator, reader) = Handler();

        var result = await handler.ExecuteAsync(Command(AssistantIntent.SummarizeDocument));

        Assert.False(result.IsSuccess);
        Assert.Equal(0, reader.ReadCount);
        Assert.Empty(navigator.Requests);
    }

    [Fact]
    public async Task AQuestionWithNoQuestionAskedIsRejected()
    {
        var (handler, _, reader) = Handler();

        var result = await handler.ExecuteAsync(Command(
            AssistantIntent.AskDocumentQuestion,
            ("file", @"C:\Notes\report.pdf")));

        Assert.False(result.IsSuccess);
        Assert.Equal(0, reader.ReadCount);
    }

    [Fact]
    public async Task ARequestThisHandlerDoesNotHandleIsRefused()
    {
        var (handler, _, _) = Handler();

        var result = await handler.ExecuteAsync(Command(AssistantIntent.Navigate, ("destination", "home")));

        Assert.False(result.IsSuccess);
        Assert.DoesNotContain(AssistantIntent.Navigate, handler.Intents);
    }

    [Fact]
    public async Task AProviderThatCannotBeReachedIsReportedWithoutItsTechnicalDetail()
    {
        var (handler, _, _) = Handler(
            provider: AIProviderType.Local,
            answerFailure: new HttpRequestException("localhost:11434 refused the connection"));

        var result = await handler.ExecuteAsync(Command(
            AssistantIntent.SummarizeDocument,
            ("file", @"C:\Notes\report.pdf")));

        // The exception's text names a host and a port, which is a fact about somebody's machine
        // and not something to read out. The code says it failed; the message stays plain.
        Assert.False(result.IsSuccess);
        Assert.DoesNotContain("11434", result.ResponseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localhost", result.ResponseText, StringComparison.OrdinalIgnoreCase);
    }

    private static VoiceCommand Command(AssistantIntent intent, params (string Key, string Value)[] parameters)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in parameters)
        {
            metadata[key] = value;
        }

        return new VoiceCommand(
            Guid.NewGuid(),
            "a spoken request",
            intent,
            metadata,
            1d,
            DateTimeOffset.UtcNow,
            ActionSafetyLevel.Safe,
            requiresConfirmation: false);
    }

    private static (
        DocumentVoiceHandler Handler,
        FakeApplicationNavigator Navigator,
        FakeDocumentReader Reader) Handler(
        string answer = "An answer.",
        AIProviderType provider = AIProviderType.Local,
        PermissionCapability? deny = null,
        IReadOnlyCollection<Core.Models.Documents.DocumentWarning>? warnings = null,
        Core.Exceptions.DocumentException? readFailure = null,
        Exception? answerFailure = null)
    {
        var permissions = new FakePermissionService();
        if (deny is not null)
        {
            permissions.Denied.Add(deny.Value);
        }

        var reader = new FakeDocumentReader
        {
            Content = FakeDocumentReader.WithText("The project deadline is October 15.", "report.pdf"),
            ExceptionToThrow = readFailure,
        };

        if (warnings is not null)
        {
            reader.Content = FakeDocumentReader.WithText(
                "The project deadline is October 15.",
                "report.pdf",
                DocumentFileType.PlainText,
                warnings);
        }

        var chunker = new FakeDocumentChunker();
        chunker.WithChunk("The project deadline is October 15.");

        var ai = new FakeAIService
        {
            Provider = provider,
            Response = Core.Models.AIResponse.Success(answer, provider),
            ExceptionToThrow = answerFailure,
        };

        var documents = new WindowsAIAssistant.Application.Documents.DocumentAnalysisService(
            reader,
            chunker,
            new WindowsAIAssistant.Application.Documents.DocumentChunkRanker(NullLogger<WindowsAIAssistant.Application.Documents.DocumentChunkRanker>.Instance),
            ai,
            new WindowsAIAssistant.Application.Documents.DocumentCloudConsentPolicy(permissions, ai, NullLogger<WindowsAIAssistant.Application.Documents.DocumentCloudConsentPolicy>.Instance),
            new WindowsAIAssistant.Application.Documents.DocumentProcessingLimits(),
            NullLogger<WindowsAIAssistant.Application.Documents.DocumentAnalysisService>.Instance);

        var navigator = new FakeApplicationNavigator();

        // The document page is a real destination, not one the fake refuses as unregistered.
        // A test that did not say so would be testing the fake's defaults.
        navigator.AvailableRoutes.Add(NavigationRoute.Document);

        var handler = new DocumentVoiceHandler(
            navigator,
            new SummarizeDocumentHandler(documents),
            new AskDocumentQuestionHandler(documents),
            permissions,
            NullLogger<DocumentVoiceHandler>.Instance);

        return (handler, navigator, reader);
    }
}
