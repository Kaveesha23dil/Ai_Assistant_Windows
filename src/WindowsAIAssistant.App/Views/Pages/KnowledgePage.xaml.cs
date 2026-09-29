using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WindowsAIAssistant.App.ViewModels;

namespace WindowsAIAssistant.App.Views.Pages;

/// <summary>
/// The knowledge page.
/// <para>
/// The page binds and nothing else. Every decision — which base, what a document's status means,
/// whether a question can be asked, what a refusal says — is made in the view model and the
/// handlers below it, so that the same decision is arrived at whether it was asked here, from the
/// chat page's knowledge mode, or by voice.
/// </para>
/// <para>
/// The page does no work in its constructor. Loading the store is a deliberate act, bound to the
/// page appearing rather than performed as a side effect of navigation, so that coming back to it
/// shows what is there now rather than what was there when the application started.
/// </para>
/// </summary>
public sealed partial class KnowledgePage : Page
{
    public KnowledgePage(KnowledgeViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    public KnowledgeViewModel ViewModel { get; }

    /// <summary>Reads the store when the page is shown, and stops any work left from last time.</summary>
    protected override async void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);

        try
        {
            await ViewModel.LoadAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // The view model already turns a failure into a sentence on the page. Reaching here
            // means the page itself could not be shown, and there is nothing above it to show the
            // problem in, so it is left to the log rather than swallowed silently.
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs args)
    {
        ViewModel.Cancel();
        base.OnNavigatedFrom(args);
    }
}
