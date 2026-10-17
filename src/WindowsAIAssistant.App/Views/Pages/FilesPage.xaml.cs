using Microsoft.UI.Xaml.Controls;
using WindowsAIAssistant.App.ViewModels;

namespace WindowsAIAssistant.App.Views.Pages;

public sealed partial class FilesPage : Page
{
    public FilesPage(FilesViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Unloaded += (_, _) => ViewModel.Deactivate();
    }

    public FilesViewModel ViewModel { get; }
}
