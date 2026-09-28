using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using WindowsAIAssistant.App.Services.Navigation;
using WindowsAIAssistant.Core.Abstractions.Files;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.App.ViewModels;

/// <summary>
/// One row in the files list: a search result, with the four things a person can do with it.
/// <para>
/// The actions are commands on the row rather than on the page, because a button that acts on
/// whichever row happens to be selected is a button that acts on the wrong file the moment two
/// people use the page differently. Each row carries its own path, and no action has to be
/// told which one it is for.
/// </para>
/// </summary>
public sealed partial class FileResultViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly IUriLauncherService _launcher;

    public FileResultViewModel(
        FileSearchResult result,
        INavigationService navigation,
        IUriLauncherService launcher)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(launcher);

        Result = result;
        _navigation = navigation;
        _launcher = launcher;
    }

    public FileSearchResult Result { get; }

    public string FullPath => Result.FullPath;

    /// <summary>
    /// Gets a value indicating whether the file is one this application can read and analyze.
    /// A row that cannot be opened is shown but its document actions are disabled, rather than
    /// being hidden, because a person looking for that file should still be able to find it.
    /// </summary>
    public bool IsSupportedDocument => SupportedExtensions.Contains(Result.Extension, StringComparer.OrdinalIgnoreCase);

    public string Name => Result.Name;

    public string ExtensionText => Result.Extension.TrimStart('.').ToUpperInvariant();

    public string LocationText =>
        System.IO.Path.GetDirectoryName(Result.FullPath) ?? Result.FullPath;

    public string SizeText => FormatSize(Result.Size);

    public string ModifiedText => Result.LastModified.ToLocalTime().ToString("d MMM yyyy");

    /// <summary>
    /// Opens the file in whatever application the person already uses for it. The file is
    /// handed to the shell as a URI, which is the only route by which the application opens
    /// anything at all.
    /// </summary>
    [RelayCommand]
    private async Task OpenAsync()
    {
        if (!Uri.TryCreate(Result.FullPath, UriKind.Absolute, out var uri))
        {
            return;
        }

        await _launcher.OpenAsync(uri).ConfigureAwait(true);
    }

    /// <summary>
    /// Opens the folder containing the file, with the file selected, so a person who wants the
    /// original rather than a summary can go straight to it.
    /// </summary>
    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        if (!Uri.TryCreate(Result.FullPath, UriKind.Absolute, out var file) ||
            !Uri.TryCreate(
                System.IO.Path.GetDirectoryName(file.LocalPath) ?? string.Empty,
                UriKind.Absolute,
                out var folder))
        {
            return;
        }

        await _launcher.OpenAsync(folder).ConfigureAwait(true);
    }

    /// <summary>Summarizes the file on the document page.</summary>
    [RelayCommand]
    private void Summarize() => _navigation.Navigate(NavigationRoute.Document, Result.FullPath);

    /// <summary>Opens the file on the document page to ask it a question.</summary>
    [RelayCommand]
    private void AskAi() => _navigation.Navigate(NavigationRoute.Document, Result.FullPath);

    private static readonly string[] SupportedExtensions =
    [
        ".pdf", ".docx", ".doc", ".pptx", ".ppt", ".xlsx", ".xls", ".txt", ".md", ".rtf", ".csv",
    ];

    /// <summary>
    /// Writes a byte count the way a person reading a file list expects it: divided down to a
    /// unit that leaves a number worth reading, and labelled with the unit that number is in.
    /// Dividing after choosing the label would put every size one unit too large.
    /// </summary>
    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        var size = (double)bytes;

        foreach (var unit in new[] { "KB", "MB", "GB", "TB" })
        {
            size /= 1024;

            if (size < 1024)
            {
                return $"{size:0.#} {unit}";
            }
        }

        return $"{size:0.#} PB";
    }
}

/// <summary>
/// Files page state. No indexing happens in this step.
/// <para>
/// Searching is still the development implementation underneath, so this page's job is the part
/// that is real: opening a document. A path is turned into a route through the navigation
/// service rather than by constructing a page, so a document opened from here follows exactly
/// the path one opened by voice or by a quick action would.
/// </para>
/// <para>
/// Results are the search service's, unchanged. A row shows the name, the folder, the size, and
/// the date, and carries the actions; it holds no extracted text, because nothing on this page
/// needs any and a list of results is a poor place to keep copies of files.
/// </para>
/// </summary>
public sealed partial class FilesViewModel : ObservableObject
{
    private readonly IFileSearchService _search;
    private readonly INavigationService _navigation;
    private readonly IUriLauncherService _launcher;
    private CancellationTokenSource? _current;

    public FilesViewModel(
        IFileSearchService search,
        INavigationService navigation,
        IUriLauncherService launcher)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(launcher);

        _search = search;
        _navigation = navigation;
        _launcher = launcher;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchText))]
    [NotifyPropertyChangedFor(nameof(CanSearch))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the path of the document to open. Kept here so the person can paste a path
    /// rather than find a file through a dialog, which matters for a document that arrived as a
    /// download or lives on a network share.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDocumentPath))]
    [NotifyCanExecuteChangedFor(nameof(OpenDocumentCommand))]
    public partial string DocumentPath { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchingVisibility))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    public partial bool IsSearching { get; set; }

    public string HeaderTitle => "Files";

    public string Description => "Search your local files, or open a document to summarize it and ask it questions.";

    public string PlaceholderText => "Search files...";

    public string DocumentPlaceholderText => "Full path to a document to open...";

    public string EmptyStateTitle => "No files found.";

    public string EmptyStateMessage => "Search your files, or paste a full path above to open one.";

    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

    public bool CanSearch => HasSearchText && !IsSearching;

    public bool HasDocumentPath => !string.IsNullOrWhiteSpace(DocumentPath);

    public bool HasResults => Results.Count > 0;

    /// <summary>
    /// Gets the visibility of the spinner. A bound boolean cannot become a
    /// <see cref="Visibility"/> on its own, and a page should not have to carry a converter for
    /// one element, so the mapping lives here beside the state it describes.
    /// </summary>
    public Visibility SearchingVisibility =>
        IsSearching ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Gets the visibility of the empty state, which is shown only when a search has finished
    /// and found nothing. Showing it before a search has been asked for would greet a person
    /// who has not done anything with a claim about what they do not have.
    /// </summary>
    public Visibility EmptyStateVisibility =>
        !IsSearching && Results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public ObservableCollection<FileResultViewModel> Results { get; } = [];

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        var query = SearchText?.Trim();
        if (string.IsNullOrWhiteSpace(query) || IsSearching)
        {
            return;
        }

        // Each search replaces the token of the one before it, so a person who types another
        // query while the first is still walking the disk is not left with whichever finishes
        // last.
        _current?.Dispose();
        _current = new CancellationTokenSource();
        var token = _current.Token;

        IsSearching = true;
        SearchCommand.NotifyCanExecuteChanged();

        try
        {
            var found = await _search.SearchAsync(query, token).ConfigureAwait(true);

            Results.Clear();
            foreach (var result in found)
            {
                Results.Add(new FileResultViewModel(result, _navigation, _launcher));
            }
        }
        catch (OperationCanceledException)
        {
            // A search that was replaced by a newer one is not a failure and has nothing to say.
        }
        finally
        {
            IsSearching = false;
            SearchCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(HasResults));
        }
    }

    /// <summary>
    /// Opens the document page on the chosen file.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasDocumentPath))]
    private void OpenDocument()
    {
        if (string.IsNullOrWhiteSpace(DocumentPath))
        {
            return;
        }

        _navigation.Navigate(NavigationRoute.Document, DocumentPath.Trim());
    }

    [RelayCommand]
    private void IndexFolder()
    {
    }
}
