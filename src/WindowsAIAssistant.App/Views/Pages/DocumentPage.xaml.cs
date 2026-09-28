using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WindowsAIAssistant.App.ViewModels;

namespace WindowsAIAssistant.App.Views.Pages;

/// <summary>
/// The document page.
/// <para>
/// The page takes a path from the navigation parameter when it is reached with one, so a
/// document opened from the file list arrives already pointed at the right file. It is the only
/// place that reads the parameter, and it does no document work of its own: the view model
/// above it holds the state and the handlers below it do the reading.
/// </para>
/// </summary>
public sealed partial class DocumentPage : Page
{
    public DocumentPage(DocumentViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    public DocumentViewModel ViewModel { get; }

    /// <summary>
    /// Reads the file to open from the navigation parameter. A page reached without one is left
    /// showing an empty path rather than refusing to appear, so the person can type or paste one:
    /// the page is a place to work on a document, and arriving there without one is not an error.
    /// </summary>
    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);

        if (args.Parameter is string filePath && !string.IsNullOrWhiteSpace(filePath))
        {
            ViewModel.Load(filePath);
        }
    }
}
