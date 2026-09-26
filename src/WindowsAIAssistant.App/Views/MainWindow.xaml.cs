using System.Collections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindowsAIAssistant.App.Controls;
using WindowsAIAssistant.App.ViewModels;
using WindowsAIAssistant.App.Views.Pages;
using Windows.Graphics;
using WinRT.Interop;

namespace WindowsAIAssistant.App.Views;

/// <summary>
/// Application shell. Page switching is intentionally local to this view; the dedicated
/// navigation service replaces it in a later step.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const int DefaultWindowWidth = 1200;
    private const int DefaultWindowHeight = 800;

    private readonly IServiceProvider _services;
    private readonly ILogger<MainWindow> _logger;
    private bool _isSelectionSynchronizing;

    public MainWindow(MainViewModel viewModel, IServiceProvider services, ILogger<MainWindow> logger)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        ViewModel = viewModel;
        InitializeComponent();
        NavView.DataContext = viewModel;

        ConfigureTitleBar();
        ApplyStartupTheme();
        ResizeToStartupSize();
        SynchronizeSelection(ViewModel.SelectedSection);
        ShowSection(ViewModel.SelectedSection);
    }

    public MainViewModel ViewModel { get; }

    private void ApplyStartupTheme()
    {
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = ThemeHelper.Resolve(ViewModel.CurrentTheme);
        }
    }

    private void ConfigureTitleBar()
    {
        try
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(TitleBarArea);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Title bar customization was skipped.");
        }
    }

    private void ResizeToStartupSize()
    {
        try
        {
            var handle = WindowNative.GetWindowHandle(this);
            var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(handle));
            appWindow?.Resize(new SizeInt32(DefaultWindowWidth, DefaultWindowHeight));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Default window size could not be applied.");
        }
    }

    private void SynchronizeSelection(AppSection section)
    {
        if (FindNavigationItem(NavView.MenuItems, section) is { } menuItem)
        {
            SetSelectedItem(menuItem);
            return;
        }

        if (FindNavigationItem(NavView.FooterMenuItems, section) is { } footerItem)
        {
            SetSelectedItem(footerItem);
        }
    }

    private static NavigationViewItem? FindNavigationItem(IEnumerable items, AppSection section)
    {
        foreach (var item in items)
        {
            if (item is NavigationViewItem navigationItem
                && navigationItem.Tag is string tag
                && string.Equals(tag, section.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return navigationItem;
            }
        }

        return null;
    }

    private void SetSelectedItem(NavigationViewItem item)
    {
        _isSelectionSynchronizing = true;
        try
        {
            NavView.SelectedItem = item;
        }
        finally
        {
            _isSelectionSynchronizing = false;
        }
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_isSelectionSynchronizing)
        {
            return;
        }

        if (args.SelectedItem is not NavigationViewItem { Tag: string tag }
            || !Enum.TryParse<AppSection>(tag, ignoreCase: true, out var section))
        {
            return;
        }

        ViewModel.SelectedSection = section;
        ShowSection(section);
    }

    private void OnPaneToggleClick(object sender, RoutedEventArgs args) =>
        NavView.IsPaneOpen = !NavView.IsPaneOpen;

    private void ShowSection(AppSection section)
    {
        var pageType = section switch
        {
            AppSection.Home => typeof(HomePage),
            AppSection.Chat => typeof(ChatPage),
            AppSection.Files => typeof(FilesPage),
            AppSection.Automations => typeof(AutomationsPage),
            AppSection.Settings => typeof(SettingsPage),
            _ => typeof(HomePage)
        };

        try
        {
            if (_services.GetService(pageType) is UIElement page)
            {
                ContentFrame.Content = page;
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The {Section} page could not be created.", section);
        }
    }
}
