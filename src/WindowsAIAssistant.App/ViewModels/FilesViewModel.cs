using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WindowsAIAssistant.App.ViewModels;

/// <summary>
/// Files page state. No indexing or searching happens in this step.
/// </summary>
public sealed partial class FilesViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    public string HeaderTitle => "Files";

    public string Description => "Search and ask questions about your local files.";

    public string PlaceholderText => "Search files...";

    public string EmptyStateTitle => "No files indexed yet.";

    public string EmptyStateMessage => "Indexing is not enabled. Review the privacy settings first.";

    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

    public bool CanSearch => HasSearchText;

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasSearchText));
        OnPropertyChanged(nameof(CanSearch));
        SearchCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private void Search()
    {
    }

    [RelayCommand]
    private void ChatWithDocument()
    {
    }

    [RelayCommand]
    private void IndexFolder()
    {
    }
}
