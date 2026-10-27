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
        Loaded += async (_, _) => { UpdateQuickActionLayout(); await ViewModel.RefreshActivityAsync(); };
    }

    public HomeViewModel ViewModel { get; }

    private void OnQuickActionsSizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs args) => UpdateQuickActionLayout();

    private void UpdateQuickActionLayout()
    {
        if (QuickActionsGrid?.ItemsPanelRoot is not ItemsWrapGrid panel || QuickActionsGrid.ActualWidth <= 0) return;
        var width = QuickActionsGrid.ActualWidth;
        ArtworkVisual.Visibility = HeroGrid.ActualWidth >= 780 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        HeroCopy.MaxWidth = HeroGrid.ActualWidth >= 1100 ? 560 : HeroGrid.ActualWidth >= 780 ? 330 : Math.Max(200, HeroGrid.ActualWidth - 56);
        var columns = width >= 1050 ? 4 : width >= 600 ? 2 : 1;
        // Leave room for the GridView's scroll presenter and fractional DPI rounding.
        panel.ItemWidth = Math.Max(1, Math.Floor((width - 24) / columns));
        panel.ItemHeight = 142;
        var stacked = width < 700;
        DashboardPanels.ColumnDefinitions[1].Width = new Microsoft.UI.Xaml.GridLength(stacked ? 0 : 1, Microsoft.UI.Xaml.GridUnitType.Star);
        Grid.SetColumn(SuggestionsPanel, stacked ? 0 : 1);
        Grid.SetRow(SuggestionsPanel, stacked ? 1 : 0);
    }
}
