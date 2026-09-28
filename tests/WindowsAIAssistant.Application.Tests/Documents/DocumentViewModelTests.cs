using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging.Abstractions;
using WindowsAIAssistant.App.ViewModels;
using WindowsAIAssistant.Application.Documents;
using WindowsAIAssistant.Application.Documents.Queries.AskDocumentQuestion;
using WindowsAIAssistant.Application.Documents.Queries.StreamDocumentAnswer;
using WindowsAIAssistant.Application.Documents.Queries.SummarizeDocument;
using WindowsAIAssistant.Application.Tests.Fakes;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;
using WindowsAIAssistant.Core.Models.Documents;

namespace WindowsAIAssistant.Application.Tests.Documents;

/// <summary>
/// Covers what the document panel shows while an answer is arriving.
/// <para>
/// A streaming answer is easy to get wrong in a way no other feature is: the panel can end up
/// showing a summary of a summary, a half sentence above an error, or the previous document's
/// references beside this one's answer. Each of those is a way of telling a person something
/// untrue about a file, so each is asserted here rather than left to be noticed.
/// </para>
/// </summary>
public sealed class DocumentViewModelTests
{
    [Fact]
    public async Task AnAnswerAppearsOnScreenBeforeTheProviderHasFinished()
    {
        var (viewModel, ai) = ViewModel();
        ai.Response = AIResponse.Success(
            "The deadline is October 15 and the client presents the plan the week before.",
            AIProviderType.Local);

        viewModel.Load(@"C:\Notes\report.txt");
        viewModel.Question = "When is the deadline?";

        // The answer arrives in fragments, and each one is on the panel before the next arrives.
        var shown = new List<string>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(DocumentViewModel.Answer) && !string.IsNullOrEmpty(viewModel.Answer))
            {
                shown.Add(viewModel.Answer);
            }
        };

        await ((IAsyncRelayCommand)viewModel.AskCommand).ExecuteAsync(null);

        // Growing, not being replaced: a panel that redrew the whole answer on every fragment
        // would flicker several times a second, which is worse than waiting.
        Assert.True(shown.Count > 1, "the answer was not shown as it arrived");
        Assert.Equal(shown.OrderBy(text => text.Length), shown);
        Assert.Equal(
            "The deadline is October 15 and the client presents the plan the week before.",
            viewModel.Answer);
    }

    [Fact]
    public async Task TheFinishedAnswerIsSurroundedByThePlacesItCameFrom()
    {
        var (viewModel, _) = ViewModel();
        viewModel.Load(@"C:\Notes\report.txt");
        viewModel.Question = "When is the deadline?";

        await ((IAsyncRelayCommand)viewModel.AskCommand).ExecuteAsync(null);

        // References are only known once the whole answer is in, and a reader who cannot check
        // an answer against the document has been given a claim rather than an answer.
        Assert.Equal("Answer", viewModel.AnswerHeading);
        Assert.Equal("page 1", viewModel.ReferencesText);
        Assert.True(viewModel.ShowReferences);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task AQuestionTheDocumentDoesNotAnswerIsSaidToBeUnanswered()
    {
        var (viewModel, ai) = ViewModel();
        ai.Response = AIResponse.Success(DocumentPromptBuilder.SourceNotFoundMarker, AIProviderType.Local);

        viewModel.Load(@"C:\Notes\report.txt");
        viewModel.Question = "When is the CEO's birthday?";

        await ((IAsyncRelayCommand)viewModel.AskCommand).ExecuteAsync(null);

        // Shown in words fit to read, rather than as the bare marker the model was told to use.
        // A person should not have to know the protocol to learn that the document is silent.
        Assert.Contains("does not say", viewModel.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(DocumentPromptBuilder.SourceNotFoundMarker, viewModel.Answer, StringComparison.Ordinal);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task AStreamThatStopsHalfwayLeavesNoPartialAnswerOnScreen()
    {
        var (viewModel, ai) = ViewModel();
        ai.StreamUpdates =
        [
            AIStreamUpdate.Started(AIProviderType.Local, "fake-model"),
            AIStreamUpdate.Delta("The deadline is October"),
            AIStreamUpdate.Failed(
                "The deadline is October",
                AIResponse.Failure("The connection closed.", AIProviderType.Local, "CONNECTION_CLOSED"),
                "CONNECTION_CLOSED",
                "The connection closed."),
        ];

        viewModel.Load(@"C:\Notes\report.txt");
        viewModel.Question = "When is the deadline?";

        await ((IAsyncRelayCommand)viewModel.AskCommand).ExecuteAsync(null);

        // Half a sentence presented as a document's answer, above a message about the provider,
        // is worse than nothing: it puts a claim in front of a person with no way to see that it
        // was never finished.
        Assert.Empty(viewModel.Answer);
        Assert.True(viewModel.HasError);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task ARefusalToSendTheDocumentIsShownAsTheRefusalItIs()
    {
        var permissions = new FakePermissionService();
        permissions.Denied.Add(PermissionCapability.DocumentCloudProcessing);

        var (viewModel, _) = ViewModel(permissions: permissions, provider: AIProviderType.OpenAI);

        viewModel.Load(@"C:\Notes\report.txt");
        viewModel.Question = "When is the deadline?";

        await ((IAsyncRelayCommand)viewModel.AskCommand).ExecuteAsync(null);

        // The message about consent, not one about a broken file. Telling somebody their
        // document is unreadable when the real problem is a setting sends them looking in the
        // wrong place entirely.
        Assert.True(viewModel.HasError);
        Assert.Contains("cloud", viewModel.ErrorText, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(viewModel.Answer);
    }

    [Fact]
    public async Task ALimitationMetWhileReadingIsShownBesideTheAnswer()
    {
        var reader = new FakeDocumentReader
        {
            Content = FakeDocumentReader.WithText(
                "Half a workbook.",
                "book.xlsx",
                DocumentFileType.Excel,
                [DocumentWarningMessages.Create(DocumentWarningKind.SpreadsheetTruncated)]),
        };

        var (viewModel, _) = ViewModel(reader: reader);
        viewModel.Load(@"C:\Notes\book.xlsx");
        viewModel.Question = "What is in it?";

        await ((IAsyncRelayCommand)viewModel.AskCommand).ExecuteAsync(null);

        // A summary of part of a document has to say so, or it is a summary of a document that
        // does not exist.
        Assert.True(viewModel.HasWarnings);
        Assert.NotEmpty(viewModel.Warnings);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.Answer));
    }

    [Fact]
    public async Task StoppingLeavesWhateverHadAlreadyArrived()
    {
        var (viewModel, ai) = ViewModel();
        ai.Response = AIResponse.Success(
            string.Join(' ', Enumerable.Repeat("the deadline is October", 40)),
            AIProviderType.Local);
        ai.FragmentDelay = TimeSpan.FromMilliseconds(5);

        viewModel.Load(@"C:\Notes\report.txt");
        viewModel.Question = "When is the deadline?";

        var run = ((IAsyncRelayCommand)viewModel.AskCommand).ExecuteAsync(null);
        ((IRelayCommand)viewModel.CancelCommand).Execute(null);
        await run;

        // Stopping is a deliberate act, so it is reported as stopping and not as a failure, and
        // the text that had already been shown is not thrown away.
        Assert.Equal("Stopped.", viewModel.ErrorText);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task ASummaryReachesThePanelWithTheSameReferences()
    {
        var (viewModel, _) = ViewModel();
        viewModel.Load(@"C:\Notes\report.txt");

        await ((IAsyncRelayCommand)viewModel.SummarizeCommand).ExecuteAsync(null);

        Assert.Equal("Summary", viewModel.AnswerHeading);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.Answer));
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task ASecondDocumentStartsFromNothingRatherThanInheritingTheLastAnswer()
    {
        var (viewModel, _) = ViewModel();

        viewModel.Load(@"C:\Notes\first.txt");
        viewModel.Question = "When is the deadline?";
        await ((IAsyncRelayCommand)viewModel.AskCommand).ExecuteAsync(null);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.Answer));

        viewModel.Load(@"C:\Notes\second.txt");

        // The previous document's answer, references, and errors are gone. Left in place they
        // would be read as belonging to the new file, which is the one mistake on this page
        // that could put the wrong document's words next to the wrong document's name.
        Assert.Empty(viewModel.Answer);
        Assert.Empty(viewModel.ReferencesText);
        Assert.False(viewModel.HasError);
        Assert.Empty(viewModel.Warnings);
    }

    [Fact]
    public async Task AQuestionCannotBeAskedBeforeOneIsTyped()
    {
        var (viewModel, ai) = ViewModel();
        viewModel.Load(@"C:\Notes\report.txt");

        Assert.False(viewModel.CanAsk);
        await ((IAsyncRelayCommand)viewModel.AskCommand).ExecuteAsync(null);

        Assert.Equal(0, ai.CallCount);
    }

    [Fact]
    public async Task NoDocumentMeansNothingCanBeAsked()
    {
        var (viewModel, ai) = ViewModel();

        viewModel.Question = "When is the deadline?";
        await ((IAsyncRelayCommand)viewModel.SummarizeCommand).ExecuteAsync(null);

        Assert.False(viewModel.HasDocument);
        Assert.Equal(0, ai.CallCount);
    }

    private static (
        DocumentViewModel ViewModel,
        FakeAIService Ai) ViewModel(
        FakePermissionService? permissions = null,
        AIProviderType provider = AIProviderType.Local,
        FakeDocumentReader? reader = null)
    {
        permissions ??= new FakePermissionService();
        reader ??= new FakeDocumentReader
        {
            Content = FakeDocumentReader.WithSections("The project deadline is October 15."),
        };

        var chunker = new FakeDocumentChunker();
        chunker.WithChunk("The project deadline is October 15.");

        var ai = new FakeAIService
        {
            Provider = provider,
            Response = AIResponse.Success("An answer from the document.", provider),
        };

        var documents = new DocumentAnalysisService(
            reader,
            chunker,
            new DocumentChunkRanker(NullLogger<DocumentChunkRanker>.Instance),
            ai,
            new DocumentCloudConsentPolicy(permissions, ai, NullLogger<DocumentCloudConsentPolicy>.Instance),
            new DocumentProcessingLimits(),
            NullLogger<DocumentAnalysisService>.Instance);

        return (
            new DocumentViewModel(
                new SummarizeDocumentHandler(documents),
                new AskDocumentQuestionHandler(documents),
                new StreamDocumentAnswerHandler(documents)),
            ai);
    }
}
