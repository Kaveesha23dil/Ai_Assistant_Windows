using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindowsAIAssistant.App.Controls;
using WindowsAIAssistant.App.ViewModels;

namespace WindowsAIAssistant.App.Views.Pages;

public sealed partial class SettingsPage : Page
{
    private bool _isApplyingTheme;

    public SettingsPage(SettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Unloaded += OnUnloaded;
    }

    public SettingsViewModel ViewModel { get; }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SettingsViewModel.Theme))
        {
            ApplyTheme(ViewModel.Theme);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs args) =>
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;

    private void ApplyTheme(string theme)
    {
        if (_isApplyingTheme)
        {
            return;
        }

        if (XamlRoot?.Content is FrameworkElement root)
        {
            _isApplyingTheme = true;
            root.RequestedTheme = ThemeHelper.Resolve(theme);
            _isApplyingTheme = false;
        }
    }
}
