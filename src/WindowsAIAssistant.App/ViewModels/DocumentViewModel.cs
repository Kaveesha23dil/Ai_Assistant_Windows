using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowsAIAssistant.Application.Documents;
using WindowsAIAssistant.Application.Documents.Queries.AskDocumentQuestion;
using WindowsAIAssistant.Application.Documents.Queries.StreamDocumentAnswer;
using WindowsAIAssistant.Application.Documents.Queries.SummarizeDocument;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Documents;

namespace WindowsAIAssistant.App.ViewModels;

/// <summary>
/// One document on screen, with the two things a person can ask of it.
/// <para>
/// The view model holds the file's path, the answer, and the places the answer came from. It
/// holds none of the document's text, because the text is not what the page shows and the
/// extracted content is not the page's to keep: the work of reading the file belongs to the
/// layers below, and a value crossing up here would be one more copy of somebody's contract
/// living in the interface layer.
/// </para>
/// <para>
/// Work is cancellable, and cancelling is a deliberate act rather than an accident: the token
/// is replaced per request so a person can abandon a slow summary of a long document and ask
/// something else instead of waiting for both.
/// </para>
/// </summary>
public sealed partial class DocumentViewModel : ObservableObject
{
    private readonly SummarizeDocumentHandler _summarize;
    private readonly AskDocumentQuestionHandler _ask;
    private readonly StreamDocumentAnswerHandler _stream;
    private CancellationTokenSource? _current;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDocument))]
    [NotifyPropertyChangedFor(nameof(HeaderTitle))]
    [NotifyCanExecuteChangedFor(nameof(SummarizeCommand))]
    [NotifyCanExecuteChangedFor(nameof(AskCommand))]
    public partial string FilePath { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedSummaryMode))]
    public partial int SummaryModeIndex { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAsk))]
    [NotifyCanExecuteChangedFor(nameof(AskCommand))]
    public partial string Question { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnswer))]
    [NotifyPropertyChangedFor(nameof(HasWarnings))]
    public partial string Answer { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnswerHeading))]
    public partial string AnswerHeading { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(SummarizeCommand))]
    [NotifyCanExecuteChangedFor(nameof(AskCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowReferences))]
    public partial string ReferencesText { get; set; } = string.Empty;

    public DocumentViewModel(
        SummarizeDocumentHandler summarize,
        AskDocumentQuestionHandler ask,
        StreamDocumentAnswerHandler stream)
    {
        ArgumentNullException.ThrowIfNull(summarize);
        ArgumentNullException.ThrowIfNull(ask);
        ArgumentNullException.ThrowIfNull(stream);

        _summarize = summarize;
        _ask = ask;
        _stream = stream;
    }

    public string HeaderTitle =>
        string.IsNullOrWhiteSpace(FileName) ? "Document" : FileName;

    public string Description =>
        "Summarize this document, or ask a question answered only from what it says.";

    public string QuestionPlaceholder => "Ask about this document...";

    public bool HasDocument => !string.IsNullOrWhiteSpace(FilePath);

    public bool HasAnswer => !string.IsNullOrWhiteSpace(Answer);

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public bool HasWarnings => Warnings.Count > 0;

    public bool ShowReferences => !string.IsNullOrWhiteSpace(ReferencesText);

    public bool CanAsk => HasDocument && !string.IsNullOrWhiteSpace(Question);

    public string StatusText => IsBusy ? "Working..." : string.Empty;

    public string FileName => string.IsNullOrWhiteSpace(FilePath)
        ? string.Empty
        : Path.GetFileName(FilePath);

    public ObservableCollection<string> Warnings { get; } = [];

    public IReadOnlyList<DocumentSummaryMode> SummaryModes { get; } =
    [
        DocumentSummaryMode.Short,
        DocumentSummaryMode.Standard,
        DocumentSummaryMode.Detailed,
    ];

    public string[] SummaryModeNames { get; } = ["Short", "Standard", "Detailed"];

    public DocumentSummaryMode SelectedSummaryMode =>
        SummaryModes[Math.Clamp(SummaryModeIndex, 0, SummaryModes.Count - 1)];

    /// <summary>
    /// Points the page at a file. Called when the page is reached with a parameter, so that
    /// arriving from the file list and typing a path behave the same afterwards.
    /// </summary>
    public void Load(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        FilePath = filePath.Trim();
        Answer = string.Empty;
        AnswerHeading = string.Empty;
        ErrorText = string.Empty;
        ReferencesText = string.Empty;
        Warnings.Clear();
        OnPropertyChanged(nameof(HasWarnings));
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task SummarizeAsync()
    {
        if (!HasDocument)
        {
            return;
        }

        await RunStreamedAsync(
            token => _stream.SummarizeAsync(
                new SummarizeDocumentCommand(FilePath, SelectedSummaryMode),
                token),
            "Summary").ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanAsk))]
    private async Task AskAsync()
    {
        if (!CanAsk)
        {
            return;
        }

        var question = Question;

        await RunStreamedAsync(
            token => _stream.AskAsync(new AskDocumentQuestionQuery(FilePath, question), token),
            "Answer").ConfigureAwait(true);
    }

    [RelayCommand]
    private void Cancel() => _current?.Cancel();

    /// <summary>
    /// Runs one request as a stream, showing the answer as it arrives.
    /// <para>
    /// The fragments are appended to what is already on screen rather than replacing it, because
    /// replacing is what makes a streaming answer flicker: the reader would watch the same
    /// paragraph redraw several times a second. A failure arrives as the last update rather than
    /// as an exception, so a provider that stops halfway clears the partial text instead of
    /// leaving half a sentence above an error.
    /// </para>
    /// </summary>
    private async Task RunStreamedAsync(
        Func<CancellationToken, IAsyncEnumerable<DocumentAnalysisUpdate>> start,
        string heading)
    {
        if (IsBusy)
        {
            return;
        }

        _current?.Dispose();
        _current = new CancellationTokenSource();
        var token = _current.Token;

        IsBusy = true;
        ErrorText = string.Empty;
        AnswerHeading = heading;
        Answer = string.Empty;
        ReferencesText = string.Empty;
        Warnings.Clear();
        OnPropertyChanged(nameof(HasWarnings));

        try
        {
            var builder = new StringBuilder();

            await foreach (var update in start(token).WithCancellation(token).ConfigureAwait(true))
            {
                switch (update.Kind)
                {
                    case DocumentAnalysisUpdateKind.Delta:
                        builder.Append(update.Text);
                        Answer = builder.ToString();
                        break;

                    case DocumentAnalysisUpdateKind.Completed when update.Result is not null:
                        Apply(update.Result, heading, builder.ToString());
                        break;

                    case DocumentAnalysisUpdateKind.Failed:
                        Answer = string.Empty;
                        AnswerHeading = string.Empty;
                        ReferencesText = string.Empty;
                        ErrorText = update.ErrorMessage ?? "This document could not be analyzed.";
                        break;

                    default:
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            ErrorText = "Stopped.";
        }
        catch (Exception exception)
        {
            // The message is the one the layers below wrote, which already says what went wrong
            // in words fit to show. The exception is not surfaced here: its text can contain a
            // file path, and this is the last place before a person sees it.
            Answer = string.Empty;
            ErrorText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Shows the finished result, replacing what was streamed in. The streamed text is preferred
    /// over the result's own copy for the answer itself, because it is what the person has
    /// already read; the result supplies the references and warnings, which only exist once the
    /// whole answer is in.
    /// </summary>
    private void Apply(DocumentAnalysisResult result, string heading, string streamedText)
    {
        Warnings.Clear();

        if (!result.IsSuccess)
        {
            Answer = string.Empty;
            AnswerHeading = string.Empty;
            ReferencesText = string.Empty;
            ErrorText = result.ErrorMessage ?? "This document could not be analyzed.";
            OnPropertyChanged(nameof(HasWarnings));
            return;
        }

        AnswerHeading = heading;
        Answer = result.Text.Contains(DocumentPromptBuilder.SourceNotFoundMarker, StringComparison.Ordinal)
            ? "This document does not say. Try asking about something it covers, or ask for a summary to see what it does contain."
            : string.IsNullOrWhiteSpace(streamedText) ? result.Text : streamedText;
        ReferencesText = result.References.Count == 0
            ? string.Empty
            : string.Join(", ", result.References);

        foreach (var warning in result.Warnings)
        {
            Warnings.Add(warning.Message);
        }

        ErrorText = string.Empty;
        OnPropertyChanged(nameof(HasWarnings));
    }
}
