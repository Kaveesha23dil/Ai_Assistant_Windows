using Microsoft.UI.Xaml.Controls;
using WindowsAIAssistant.App.ViewModels;

namespace WindowsAIAssistant.App.Views.Pages;

public sealed partial class AutomationsPage : Page
{
    public AutomationsPage(AutomationsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => { _timer.Tick += OnTick; _timer.Start(); await ViewModel.RefreshAsync(); };
        Unloaded += (_, _) => { _timer.Stop(); _timer.Tick -= OnTick; };
    }

    public AutomationsViewModel ViewModel { get; }
    private readonly Microsoft.UI.Xaml.DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _refreshing;
    private async void OnTick(object? sender, object args)
    {
        if (_refreshing) return;
        _refreshing = true;
        try { await ViewModel.RefreshAsync(); }
        finally { _refreshing = false; }
    }
}
