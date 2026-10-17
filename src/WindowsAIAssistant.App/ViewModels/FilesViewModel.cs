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

public sealed partial class FileResultViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly IUriLauncherService _launcher;
    private readonly IFilePreviewService? _preview;
    public FileResultViewModel(FileSearchResult result, INavigationService navigation, IUriLauncherService launcher, IFilePreviewService? preview = null)
    {
        Result = result;
        _navigation = navigation;
        _launcher = launcher;
        _preview = preview;
    }
    public FileSearchResult Result { get; }
    public string FullPath => Result.FullPath;
    public string Name => Result.Name;
    public string ExtensionText => Result.Extension.TrimStart('.').ToUpperInvariant();
    public string LocationText => System.IO.Path.GetDirectoryName(FullPath) ?? FullPath;
    public string ModifiedText => Result.LastModified.ToLocalTime().ToString("d MMM yyyy HH:mm");
    public string CreatedText => Result.Created?.ToLocalTime().ToString("d MMM yyyy HH:mm") ?? "Unavailable";
    public string SizeText => Result.Size < 1024 ? $"{Result.Size} B" : Result.Size < 1048576 ? $"{Result.Size / 1024d:0.#} KB" : Result.Size < 1073741824 ? $"{Result.Size / 1048576d:0.#} MB" : $"{Result.Size / 1073741824d:0.#} GB";
    public bool IsSupportedDocument => new[] { ".pdf", ".docx", ".xlsx", ".pptx", ".txt", ".md", ".csv", ".json", ".xml", ".cs", ".xaml", ".csproj", ".sln", ".log" }.Contains(Result.Extension, StringComparer.OrdinalIgnoreCase);
    [ObservableProperty] public partial string ActionMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial string PreviewText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsPreviewing { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewVisibility))]
    public partial bool IsPreviewOpen { get; set; }
    public Visibility PreviewVisibility => IsPreviewOpen ? Visibility.Visible : Visibility.Collapsed;

    [RelayCommand]
    private Task OpenAsync() => LaunchAsync(FullPath);
    [RelayCommand]
    private Task OpenFolderAsync() => LaunchAsync(LocationText);
    private async Task LaunchAsync(string path)
    {
        try
        {
            if (!Uri.TryCreate(path, UriKind.Absolute, out var uri) || !uri.IsFile) { ActionMessage = "This file path cannot be opened."; return; }
            var result = await _launcher.OpenAsync(uri);
            ActionMessage = result.IsSuccess ? string.Empty : result.ErrorMessage ?? "Windows could not open this item.";
        }
        catch (Exception) { ActionMessage = "Could not open this item. Check that it still exists and that Windows has an app for it."; }
    }
    [RelayCommand(CanExecute = nameof(IsSupportedDocument))]
    private void Summarize() => _navigation.Navigate(NavigationRoute.Document, FullPath);
    [RelayCommand(CanExecute = nameof(IsSupportedDocument))]
    private void AskAi() => _navigation.Navigate(NavigationRoute.Document, FullPath);
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task PreviewAsync(CancellationToken cancellationToken)
    {
        if (_preview is null) { ActionMessage = "Preview is unavailable."; return; }
        IsPreviewOpen = true;
        IsPreviewing = true;
        PreviewText = string.Empty;
        try
        {
            var result = await _preview.ReadAsync(FullPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            PreviewText = result.Text;
            ActionMessage = result.Notice;
        }
        catch (OperationCanceledException) { ActionMessage = "Preview cancelled."; }
        catch (UnauthorizedAccessException exception) { ActionMessage = exception.Message; }
        catch (Core.Exceptions.DocumentException exception) { ActionMessage = exception.Message; }
        catch (Exception) { ActionMessage = "Could not preview this file. It may have moved, be protected, or be unreadable."; }
        finally { IsPreviewing = false; }
    }
    [RelayCommand]
    private void ClosePreview() { PreviewCommand.Cancel(); IsPreviewOpen = false; PreviewText = string.Empty; }
}

public sealed partial class FilesViewModel : ObservableObject
{
    private readonly IFileSearchService _search;
    private readonly INavigationService _navigation;
    private readonly IUriLauncherService _launcher;
    private readonly IFilePreviewService? _preview;
    private CancellationTokenSource? _current;
    public FilesViewModel(IFileSearchService search, INavigationService navigation, IUriLauncherService launcher, IFilePreviewService? preview = null)
    {
        _search = search;
        _navigation = navigation;
        _launcher = launcher;
        _preview = preview;
    }
    public string[] LocationChoices { get; } = ["Common user folders (or location in query)", "Downloads", "Documents", "Desktop", "Pictures", "Music", "Videos", "Custom folder"];
    public string[] DateChoices { get; } = ["Any date", "Today", "Last 7 days", "Last week (Monday-Sunday)", "This month"];
    public string[] DateFieldChoices { get; } = ["Automatic (downloaded = created)", "Modified", "Created"];
    public string[] SizeChoices { get; } = ["Any size", "Under 1 MB", "1-10 MB", "Over 10 MB"];
    [ObservableProperty] public partial int LocationIndex { get; set; }
    [ObservableProperty] public partial string FolderPath { get; set; } = string.Empty;
    [ObservableProperty] public partial string Extension { get; set; } = string.Empty;
    [ObservableProperty] public partial int DateIndex { get; set; }
    [ObservableProperty] public partial int DateFieldIndex { get; set; }
    [ObservableProperty] public partial int SizeIndex { get; set; }
    [ObservableProperty] public partial bool IncludeSubfolders { get; set; } = true;
    [ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial bool HasSearched { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchText))]
    [NotifyPropertyChangedFor(nameof(CanSearch))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDocumentPath))]
    [NotifyCanExecuteChangedFor(nameof(OpenDocumentCommand))]
    public partial string DocumentPath { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchingVisibility))]
    [NotifyPropertyChangedFor(nameof(EmptyStateVisibility))]
    [NotifyPropertyChangedFor(nameof(CanSearch))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    public partial bool IsSearching { get; set; }
    public string HeaderTitle => "Files";
    public string Description => "Find files by name, type, date, and size. Preview document text locally or open files in Windows.";
    public string PlaceholderText => "Try: Find the invoice PDF I downloaded last week (or * for all files)";
    public string DocumentPlaceholderText => "Full path to a document to open...";
    public string EmptyStateTitle => "No files found.";
    public string EmptyStateMessage => "Try fewer words, another folder, or a wider date range.";
    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);
    public bool CanSearch => HasSearchText && !IsSearching;
    public bool HasDocumentPath => !string.IsNullOrWhiteSpace(DocumentPath);
    public bool HasResults => Results.Count > 0;
    public Visibility SearchingVisibility => IsSearching ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyStateVisibility => HasSearched && !IsSearching && Results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public ObservableCollection<FileResultViewModel> Results { get; } = [];

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        if (!CanSearch) return;
        _current?.Dispose();
        _current = new CancellationTokenSource();
        IsSearching = true;
        HasSearched = false;
        StatusMessage = "Searching local file names and metadata...";
        foreach (var row in Results) row.ClosePreviewCommand.Execute(null);
        Results.Clear();
        try
        {
            if (LocationIndex == 7 && string.IsNullOrWhiteSpace(FolderPath)) throw new ArgumentException("Enter a folder path for a custom-folder search.");
            var request = BuildRequest();
            var response = await _search.SearchAsync(request, _current.Token);
            _current.Token.ThrowIfCancellationRequested();
            foreach (var result in response.Files) Results.Add(new FileResultViewModel(result, _navigation, _launcher, _preview));
            HasSearched = true;
            StatusMessage = string.IsNullOrEmpty(response.Summary) ? $"{Results.Count} file(s) found." : response.Summary;
        }
        catch (OperationCanceledException) { StatusMessage = "Search cancelled."; }
        catch (UnauthorizedAccessException exception) { StatusMessage = exception.Message; }
        catch (ArgumentException exception) { StatusMessage = exception.Message; }
        catch (Exception) { StatusMessage = "Could not search this location. Check that the folder exists and is accessible."; }
        finally
        {
            IsSearching = false;
            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(EmptyStateVisibility));
        }
    }

    private FileSearchRequest BuildRequest()
    {
        KnownFolderKind? location = LocationIndex switch
        {
            1 => KnownFolderKind.Downloads, 2 => KnownFolderKind.Documents, 3 => KnownFolderKind.Desktop,
            4 => KnownFolderKind.Pictures, 5 => KnownFolderKind.Music, 6 => KnownFolderKind.Videos, _ => null,
        };
        var today = DateTime.Today;
        DateTime? from = null, before = null;
        switch (DateIndex)
        {
            case 1: from = today; before = today.AddDays(1); break;
            case 2: from = today.AddDays(-6); before = today.AddDays(1); break;
            case 3: before = today.AddDays(-((int)today.DayOfWeek + 6) % 7); from = before.Value.AddDays(-7); break;
            case 4: from = new DateTime(today.Year, today.Month, 1); before = from.Value.AddMonths(1); break;
        }
        return new FileSearchRequest(SearchText.Trim())
        {
            Location = location, FolderPath = LocationIndex == 7 ? FolderPath.Trim() : null,
            Extension = string.IsNullOrWhiteSpace(Extension) ? null : Extension.Trim(), IncludeSubfolders = IncludeSubfolders,
            From = from is { } start ? new DateTimeOffset(start).ToUniversalTime() : null,
            Before = before is { } end ? new DateTimeOffset(end).ToUniversalTime() : null,
            DateField = DateFieldIndex == 1 ? FileSearchDateField.Modified : DateFieldIndex == 2 ? FileSearchDateField.Created : null,
            MinimumSize = SizeIndex == 2 ? 1048576 : SizeIndex == 3 ? 10485761 : null,
            MaximumSize = SizeIndex == 1 ? 1048575 : SizeIndex == 2 ? 10485760 : null,
        };
    }
    [RelayCommand]
    private void CancelSearch() => _current?.Cancel();
    public void Deactivate()
    {
        _current?.Cancel();
        foreach (var row in Results) row.ClosePreviewCommand.Execute(null);
    }
    [RelayCommand(CanExecute = nameof(HasDocumentPath))]
    private void OpenDocument() => _navigation.Navigate(NavigationRoute.Document, DocumentPath.Trim());
}
