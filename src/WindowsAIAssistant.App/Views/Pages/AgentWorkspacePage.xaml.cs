using Microsoft.UI.Xaml.Controls;
using WindowsAIAssistant.App.ViewModels;

namespace WindowsAIAssistant.App.Views.Pages;

/// <summary>
/// The agent workspace page.
/// <para>
/// The view model is a constructor argument rather than something the page builds, so the page
/// holds no logic at all and the state can be exercised in a test without a window. It is a
/// singleton because a run belongs to the machine rather than to a page: navigating away and
/// back mid-run should reattach to the run in progress, not start a second one.
/// </para>
/// </summary>
public sealed partial class AgentWorkspacePage : Page
{
    public AgentWorkspacePage(AgentWorkspaceViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    public AgentWorkspaceViewModel ViewModel { get; }
}
