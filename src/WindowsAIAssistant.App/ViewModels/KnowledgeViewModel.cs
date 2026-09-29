using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowsAIAssistant.Application.DTOs;
using WindowsAIAssistant.Application.Knowledge.Commands.ManageKnowledgeBase;
using WindowsAIAssistant.Application.Knowledge.Commands.ManageKnowledgeDocuments;
using WindowsAIAssistant.Application.Knowledge.Handlers;
using WindowsAIAssistant.Application.Knowledge.Queries.AskKnowledgeQuestion;
using WindowsAIAssistant.Application.Knowledge.Queries.GetKnowledgeOverview;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Knowledge;

namespace WindowsAIAssistant.App.ViewModels;

/// <summary>
/// The knowledge page: the bases, the documents in one of them, and the questions to ask of
/// them.
/// <para>
/// The view model holds identifiers, names, statuses, answers, and citations. It holds no
/// document text and no vector, and it is not given a file path by the page it belongs to,
/// because a path is needed to reindex and nothing here displays one. That separation is what
/// keeps the same rule true on this page as on the others: the interface is a place where
/// information is read, not a second copy of it.
/// </para>
/// <para>
/// Indexing is cancellable and reports counts rather than a percentage, for the reason the
/// indexing service does: a bar that guesses at how much of a 40-page PDF is left teaches people
/// that the numbers on it are decorative.
/// </para>
/// </summary>
public sealed partial class KnowledgeViewModel : ObservableObject
{
    private readonly GetKnowledgeOverviewHandler _overview;
    private readonly ManageKnowledgeBaseHandler _bases;
    private readonly ManageKnowledgeDocumentsHandler _documents;
    private readonly AskKnowledgeQuestionHandler _ask;
    private CancellationTokenSource? _current;
    private bool _reading;

    public KnowledgeViewModel(
        GetKnowledgeOverviewHandler overview,
        ManageKnowledgeBaseHandler bases,
        ManageKnowledgeDocumentsHandler documents,
        AskKnowledgeQuestionHandler ask)
    {
        ArgumentNullException.ThrowIfNull(overview);
        ArgumentNullException.ThrowIfNull(bases);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(ask);

        _overview = overview;
        _bases = bases;
        _documents = documents;
        _ask = ask;
    }

    /// <summary>Gets the bases, for the list on the left.</summary>
    public ObservableCollection<KnowledgeBaseDto> Bases { get; } = [];

    /// <summary>Gets the documents in the selected base.</summary>
    public ObservableCollection<KnowledgeDocumentDto> Documents { get; } = [];

    /// <summary>Gets the passages an answer was taken from.</summary>
    public ObservableCollection<KnowledgeSourceDto> Sources { get; } = [];

    /// <summary>Gets the passages a question would be answered from, for "search without asking".</summary>
    public ObservableCollection<KnowledgeSearchResultDto> Matches { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderSubtitle))]
    [NotifyPropertyChangedFor(nameof(HasBases))]
    [NotifyPropertyChangedFor(nameof(HasDocuments))]
    [NotifyPropertyChangedFor(nameof(ShowsEmptyText))]
    [NotifyPropertyChangedFor(nameof(EmptyText))]
    [NotifyPropertyChangedFor(nameof(TotalText))]
    public partial KnowledgeBaseDto? SelectedBase { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAsk))]
    [NotifyCanExecuteChangedFor(nameof(AskCommand))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    public partial string Question { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnswer))]
    [NotifyPropertyChangedFor(nameof(HasSources))]
    public partial string Answer { get; set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the passages were found by wording alone, which the page
    /// states rather than passing off as a semantic search.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsKeywordOnlyNotice))]
    public partial bool IsKeywordOnly { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsTruncationNotice))]
    public partial bool WasContextTruncated { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMatches))]
    public partial bool IsShowingMatches { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(ErrorText))]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(ErrorText))]
    public partial string ErrorText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AskCommand))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddDocumentsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReindexCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameBaseCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteBaseCommand))]
    public partial bool HasSelection { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReindexCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveDocumentCommand))]
    [NotifyPropertyChangedFor(nameof(SelectedDocumentStatusText))]
    public partial KnowledgeDocumentDto? SelectedDocument { get; set; }

    /// <summary>
    /// Gets or sets the name typed into the name box, which is the name of a base about to be
    /// created and, once one is selected, the name it would be renamed to.
    /// <para>
    /// One box for both rather than two, because both are the same question asked at two
    /// different moments, and a second box on a page that already has a list of names is a second
    /// place for the name being typed to go missing.
    /// </para>
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateBaseCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameBaseCommand))]
    public partial string NewBaseName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the files chosen for indexing. Kept as text so the page can accept a path
    /// typed in as well as one picked from a dialog, and split before anything is indexed.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddDocumentsCommand))]
    public partial string FilesToIndex { get; set; } = string.Empty;

    public string HeaderSubtitle =>
        HasDocuments
            ? $"{TotalText} Ask a question and the answer is taken from them, with the places it came from."
            : "Add documents to search. Nothing you add is sent anywhere unless you allow it.";

    public bool HasBases => Bases.Count > 0;

    public bool HasDocuments => Documents.Count > 0;

    public bool HasAnswer => !string.IsNullOrWhiteSpace(Answer);

    public bool HasSources => Sources.Count > 0;

    public bool HasMatches => Matches.Count > 0;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public bool ShowsKeywordOnlyNotice => HasAnswer && IsKeywordOnly;

    public bool ShowsTruncationNotice => HasAnswer && WasContextTruncated;

    public bool CanAsk => !IsBusy && !string.IsNullOrWhiteSpace(Question);

    public string TotalText => Documents.Count == 1
        ? "1 document"
        : $"{Documents.Count} documents";

    public string EmptyText => HasBases
        ? "This base is empty. Add a document to search."
        : "No documents yet. Create a base and add something to it.";

    /// <summary>
    /// Gets a value indicating whether the "nothing here yet" line is shown.
    /// <para>
    /// The message alone was not enough. It sat on the page permanently, under a list of
    /// documents, saying the base was empty while the list showed it was not — the sort of thing
    /// that makes people stop believing the rest of the page.
    /// </para>
    /// </summary>
    public bool ShowsEmptyText => !HasDocuments;

    public string SelectedDocumentStatusText => SelectedDocument is null
        ? string.Empty
        : $"{SelectedDocument.StatusMessage} ({SelectedDocument.ChunkCount} passages)";

    /// <summary>Reads the current state of the store into the page.</summary>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken) =>
        await RunAsync(ReloadAsync, "Reading the knowledge base.").ConfigureAwait(true);

    /// <summary>
    /// The reload itself, with no busy handling of its own.
    /// <para>
    /// Separate from <see cref="LoadAsync"/> because most of the things that change the page
    /// already are the busy thing: indexing, reindexing, and removing all end by reading the
    /// store again. Calling the command from inside them would return immediately, because
    /// <see cref="RunAsync"/> refuses to start while <see cref="IsBusy"/> is set, and the page
    /// would sit there showing the state from before the work that had just finished.
    /// </para>
    /// </summary>
    private async Task ReloadAsync(CancellationToken token)
    {
        var overview = await _overview.HandleAsync(SelectedBase?.Id, token).ConfigureAwait(true);

        Replace(Bases, overview.Bases);
        Replace(Documents, overview.Documents);

        // Suppressed because this is the reload, and the assignment below is the reload's own
        // conclusion rather than a person choosing a different base from the list.
        _reading = true;

        try
        {
            SelectedBase = overview.Bases.FirstOrDefault(candidate => candidate.Id == overview.SelectedBaseId)
                ?? overview.Bases.FirstOrDefault();
        }
        finally
        {
            _reading = false;
        }

        Replace(Sources, []);
        Replace(Matches, []);
        IsShowingMatches = false;
        Answer = string.Empty;
        IsKeywordOnly = false;
        WasContextTruncated = false;
        OnPropertyChanged(nameof(HasBases));
        OnPropertyChanged(nameof(HasDocuments));
        OnPropertyChanged(nameof(ShowsEmptyText));
        OnPropertyChanged(nameof(HeaderSubtitle));
        OnPropertyChanged(nameof(EmptyText));
    }

    /// <summary>
    /// Creates a base, and selects it. An empty name is not an error: it is the way the page
    /// asks for the default base, which is what a person who has never named one wants.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCreateBase))]
    private async Task CreateBaseAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        var result = await _bases.CreateAsync(NewBaseName, cancellationToken: cancellationToken)
            .ConfigureAwait(true);

        if (!result.IsSuccess)
        {
            ErrorText = result.ErrorMessage ?? "That base could not be created.";
            return;
        }

        NewBaseName = string.Empty;
        ErrorText = string.Empty;
        await LoadAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Indexes the files named on the page.
    /// <para>
    /// The report is spoken as a summary rather than as a list of every document, because a
    /// folder of fifty files would push the useful part — the three that failed — off the top of
    /// the page. The failures are in the document list, where the person is already looking.
    /// </para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddDocuments))]
    private async Task AddDocumentsAsync(CancellationToken cancellationToken)
    {
        if (IsBusy || SelectedBase is null)
        {
            return;
        }

        var paths = FilesToIndex
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(path => path.Length > 0)
            .ToArray();

        if (paths.Length == 0)
        {
            ErrorText = "Choose at least one file to index.";
            return;
        }

        await RunAsync(async token =>
        {
            var report = await _documents
                .AddFilesAsync(
                    SelectedBase.Id,
                    paths,
                    new Progress<KnowledgeIndexingProgressDto>(ReportProgress),
                    token)
                .ConfigureAwait(true);

            FilesToIndex = string.Empty;
            ErrorText = report.Failures.Count == 0
                ? string.Empty
                : $"{report.Failures.Count} file{(report.Failures.Count == 1 ? "" : "s")} could not be indexed.";

            StatusText = report.Summary;
            await ReloadAsync(token).ConfigureAwait(true);
        }, "Indexing.").ConfigureAwait(true);
    }

    /// <summary>Rebuilds one document's index from the file it was built from.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ReindexAsync(CancellationToken cancellationToken)
    {
        if (IsBusy || SelectedDocument is null)
        {
            return;
        }

        var documentId = SelectedDocument.Id;

        await RunAsync(async token =>
        {
            var report = await _documents
                .ReindexAsync(documentId, new Progress<KnowledgeIndexingProgressDto>(ReportProgress), token)
                .ConfigureAwait(true);

            StatusText = report.Summary;
            ErrorText = report.Failures.FirstOrDefault()?.Message ?? string.Empty;
            await ReloadAsync(token).ConfigureAwait(true);
        }, "Reindexing.").ConfigureAwait(true);
    }

    /// <summary>
    /// Takes a document out of the base. The file is not touched, and the button is labelled to
    /// say so, because a page that lists somebody's documents quite reasonably reads "remove" as
    /// "delete my file".
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RemoveDocumentAsync(CancellationToken cancellationToken)
    {
        if (IsBusy || SelectedDocument is null)
        {
            return;
        }

        var removed = await _documents.RemoveAsync(SelectedDocument.Id, cancellationToken)
            .ConfigureAwait(true);

        if (!removed)
        {
            ErrorText = "That document could not be removed from the base.";
            return;
        }

        ErrorText = string.Empty;
        StatusText = "Removed from the base. The file is untouched.";
        await ReloadAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Asks a question and streams the answer as it is written.</summary>
    [RelayCommand(CanExecute = nameof(CanAsk))]
    private async Task AskAsync(CancellationToken cancellationToken)
    {
        if (!CanAsk || SelectedBase is null)
        {
            return;
        }

        var question = Question;
        var knowledgeBaseId = SelectedBase.Id;
        IsShowingMatches = false;
        Replace(Matches, []);

        await RunStreamedAsync(
            token => _ask.HandleStreamingAsync(question, knowledgeBaseId, null, token),
            "Answer").ConfigureAwait(true);
    }

    /// <summary>
    /// Finds the passages a question would be answered from, without writing an answer.
    /// <para>
    /// The most useful thing on this page for somebody deciding whether their documents are
    /// indexed properly: if a phrase they expect is not in the results, the file was not read
    /// the way they thought, and no answer would have been able to find it either.
    /// </para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAsk))]
    private async Task SearchAsync(CancellationToken cancellationToken)
    {
        if (!CanAsk || SelectedBase is null)
        {
            return;
        }

        var question = Question;
        var knowledgeBaseId = SelectedBase.Id;

        await RunAsync(async token =>
        {
            var results = await _ask.SearchAsync(question, knowledgeBaseId, null, token)
                .ConfigureAwait(true);

            Replace(Matches, results.Select(KnowledgeMapper.ToDto).ToArray());
            IsShowingMatches = true;
            StatusText = results.Count == 0
                ? "Nothing in the base matches that."
                : $"{results.Count} passage{(results.Count == 1 ? "" : "s")} matched.";
        }, "Searching.").ConfigureAwait(true);
    }

    /// <summary>
    /// Stops the work in progress. Called by the page when it is navigated away from, so an
    /// indexing run or a question is not left writing into a view model nobody is looking at.
    /// </summary>
    [RelayCommand]
    public void Cancel() => _current?.Cancel();

    private bool CanCreateBase => !IsBusy;

    private bool CanAddDocuments => !IsBusy && SelectedBase is not null && !string.IsNullOrWhiteSpace(FilesToIndex);

    partial void OnSelectedBaseChanged(KnowledgeBaseDto? value)
    {
        OnPropertyChanged(nameof(HasDocuments));
        OnPropertyChanged(nameof(ShowsEmptyText));
        OnPropertyChanged(nameof(HeaderSubtitle));
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(EmptyText));

        // The document list belongs to the selected base, so choosing a different one has to
        // bring up that base's documents. Without this the list kept showing the previous base
        // while the question box quietly asked the new one, and the two disagreed in front of
        // somebody reading them.
        if (!_reading && value is not null && !IsBusy)
        {
            _ = RunAsync(ReloadAsync, "Reading the knowledge base.").ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Renames the selected base.
    /// <para>
    /// The documents and the index are kept, and the identity is the same one, so an answer that
    /// cited a base before the rename still resolves afterwards.
    /// </para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RenameBaseAsync(CancellationToken cancellationToken)
    {
        if (IsBusy || SelectedBase is null || string.IsNullOrWhiteSpace(NewBaseName))
        {
            return;
        }

        var result = await _bases
            .RenameAsync(SelectedBase.Id, NewBaseName, cancellationToken: cancellationToken)
            .ConfigureAwait(true);

        if (!result.IsSuccess)
        {
            ErrorText = result.ErrorMessage ?? "That base could not be renamed.";
            return;
        }

        NewBaseName = string.Empty;
        ErrorText = string.Empty;
        StatusText = "Renamed.";
        await ReloadAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Removes the selected base and everything indexed in it. The files are not touched, and the
    /// button says so, because a page listing somebody's documents quite reasonably reads
    /// "remove" as "delete my files".
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteBaseAsync(CancellationToken cancellationToken)
    {
        if (IsBusy || SelectedBase is null)
        {
            return;
        }

        var name = SelectedBase.Name;
        var result = await _bases.DeleteAsync(SelectedBase.Id, cancellationToken).ConfigureAwait(true);

        if (!result.IsSuccess)
        {
            ErrorText = result.ErrorMessage ?? "That base could not be removed.";
            return;
        }

        ErrorText = string.Empty;
        StatusText = $"Removed \"{name}\" and its index. The files are untouched.";
        await ReloadAsync(cancellationToken).ConfigureAwait(true);
    }

    partial void OnSelectedDocumentChanged(KnowledgeDocumentDto? value) => HasSelection = value is not null;

    private void ReportProgress(KnowledgeIndexingProgressDto progress) =>
        StatusText = progress.Message;

    private async Task RunAsync(Func<CancellationToken, Task> work, string busyText)
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
        StatusText = busyText;

        try
        {
            await work(token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Stopped.";
        }
        catch (Exception exception)
        {
            // The exception is not shown. It can carry a path, a provider name, or a connection
            // string, and this page is read by people and screenshotted.
            ErrorText = "That did not work. The details are in the log.";
            System.Diagnostics.Debug.WriteLine(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Runs the question as a stream, showing the sources before the first word of the answer.
    /// <para>
    /// The fragments are appended to what is on screen rather than replacing it, because
    /// replacing is what makes a streaming answer flicker. A failure arrives as the last update
    /// rather than as an exception, so a provider that stops halfway clears the partial text
    /// instead of leaving half a sentence above an error.
    /// </para>
    /// </summary>
    private async Task RunStreamedAsync(
        Func<CancellationToken, IAsyncEnumerable<RagStreamUpdate>> start,
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
        StatusText = "Searching your documents.";

        try
        {
            var builder = new StringBuilder();

            await foreach (var update in start(token).WithCancellation(token).ConfigureAwait(true))
            {
                switch (update.Kind)
                {
                    case RagStreamUpdateKind.Sources:
                        Replace(Sources, KnowledgeMapper.ToSources(update.Sources));
                        break;

                    case RagStreamUpdateKind.Delta:
                        builder.Append(update.Text);
                        Answer = builder.ToString();
                        break;

                    case RagStreamUpdateKind.Completed when update.Answer is not null:
                        Answer = update.Answer.Text;
                        IsKeywordOnly = update.Answer.WasLexicalOnly;
                        WasContextTruncated = update.Answer.WasContextTruncated;
                        Replace(Sources, KnowledgeMapper.ToSources(update.Answer.Sources));
                        StatusText = string.Empty;
                        break;

                    case RagStreamUpdateKind.Failed:
                        Answer = string.Empty;
                        IsKeywordOnly = false;
                        WasContextTruncated = false;
                        Replace(Sources, []);
                        ErrorText = update.ErrorMessage ?? "That question could not be answered.";
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Stopped.";
        }
        catch (Exception exception)
        {
            ErrorText = "That question could not be answered. The details are in the log.";
            System.Diagnostics.Debug.WriteLine(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();

        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
