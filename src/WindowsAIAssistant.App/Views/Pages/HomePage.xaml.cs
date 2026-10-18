using Microsoft.UI.Xaml.Controls;
using WindowsAIAssistant.App.ViewModels;

namespace WindowsAIAssistant.App.Views.Pages;

public sealed partial class HomePage : Page
{
    public HomePage(HomeViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += (_, _) => UpdateQuickActionLayout();
    }

    public HomeViewModel ViewModel { get; }

    private void OnQuickActionsSizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs args) => UpdateQuickActionLayout();

    private void UpdateQuickActionLayout()
    {
        if (QuickActionsGrid.ItemsPanelRoot is not ItemsWrapGrid panel || QuickActionsGrid.ActualWidth <= 0) return;
        var width = QuickActionsGrid.ActualWidth;
        var columns = width >= 960 ? 3 : width >= 560 ? 2 : 1;
        // Leave room for the GridView's scroll presenter and fractional DPI rounding.
        panel.ItemWidth = Math.Max(1, Math.Floor((width - 24) / columns));
    }
}
