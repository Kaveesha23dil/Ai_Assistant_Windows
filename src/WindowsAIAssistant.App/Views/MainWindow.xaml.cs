using System.Collections;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WindowsAIAssistant.App.Controls;
using WindowsAIAssistant.App.Services.Navigation;
using WindowsAIAssistant.App.ViewModels;
using Windows.Graphics;
using NavigationRoute = WindowsAIAssistant.Core.Enums.NavigationRoute;
using NavigationRouteExtensions = WindowsAIAssistant.Core.Enums.NavigationRouteExtensions;
using WinRT.Interop;

namespace WindowsAIAssistant.App.Views;

/// <summary>
/// The application shell.
/// <para>
/// The window's responsibility is narrow: it owns the frame and the navigation view, it hands
/// the frame to the navigation service, and it forwards what the two report. It contains no
/// route-to-page mapping, no page creation, and no navigation decision of its own. That is why
/// a page opened by voice, by a quick action, or by a link follows exactly the same path as one
/// opened by clicking a menu item.
/// </para>
/// <para>
/// Selection is synchronized in one direction only. A click asks the ViewModel to navigate; the
/// highlight is then set from the service's own navigation event. Making the highlight the
/// source of navigation instead would let a programmatic change leave the menu showing the
/// previous page.
/// </para>
/// </summary>
public sealed partial class MainWindow : Window
{
    private const int DefaultWindowWidth = 1200;
    private const int DefaultWindowHeight = 800;

    private readonly INavigationService _navigation;
    private readonly ILogger<MainWindow> _logger;
    private readonly DispatcherQueue _dispatcher;
    private bool _isSelectionSynchronizing;
    private bool _backHandled;

    public MainWindow(
        MainViewModel viewModel,
        INavigationService navigation,
        ILogger<MainWindow> logger)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(logger);

        ViewModel = viewModel;
        _navigation = navigation;
        _logger = logger;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        InitializeComponent();
        NavView.DataContext = viewModel;

        ConfigureTitleBar();
        ApplyStartupTheme();

        // The frame is handed over as soon as it exists, so the very first page is shown through
        // the same path as every later one.
        _navigation.Initialize(ContentFrame);
        _navigation.Navigated += OnNavigated;

        ResizeToStartupSize();

        // The window's content is not loaded while this constructor is still running, so the
        // frame is not yet able to present anything. Asking for the opening page here would
        // leave the shell showing an empty frame that never fills in. Waiting for the shell to
        // load puts the first page on exactly the same code path as every subsequent one, and
        // the low-priority turn lets the window finish activating first.
        NavView.Loaded += OnShellLoaded;
    }

    public MainViewModel ViewModel { get; }

    /// <summary>
    /// Shows the configured opening page once the shell is on screen.
    /// </summary>
    private void OnShellLoaded(object sender, RoutedEventArgs args)
    {
        NavView.Loaded -= OnShellLoaded;

        _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (ContentFrame.Content is not null)
            {
                return;
            }

            _navigation.Navigate(ViewModel.InitialRoute);
        });
    }

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

    /// <summary>
    /// Reacts to a page change the navigation service reports, whichever entry point caused it.
    /// </summary>
    private void OnNavigated(object? sender, NavigationEventArgs e)
    {
        SynchronizeSelection(e.CurrentRoute);

        // The title is a small, deliberate echo of the route: it helps the window be identified
        // in the task bar and to a screen reader, and nothing more.
        Title = ViewModel.WindowTitle;
    }

    /// <summary>
    /// Points the navigation view at the route that is on screen, in either the menu or the
    /// footer. The flag stops that programmatic change from being read back as a click, which
    /// would navigate a second time.
    /// </summary>
    private void SynchronizeSelection(NavigationRoute route)
    {
        var item = FindNavigationItem(NavView.MenuItems, route)
            ?? FindNavigationItem(NavView.FooterMenuItems, route);

        if (item is not null)
        {
            SetSelectedItem(item);
        }
    }

    private static NavigationViewItem? FindNavigationItem(
        IEnumerable items,
        NavigationRoute route)
    {
        foreach (var item in items)
        {
            if (item is NavigationViewItem navigationItem
                && navigationItem.Tag is string tag
                && NavigationRouteExtensions.TryParse(tag, out var candidate)
                && candidate == route)
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
            || !NavigationRouteExtensions.TryParse(tag, out var route))
        {
            return;
        }

        ViewModel.NavigateCommand.Execute(route);
    }

    private void OnNavigationBackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args) =>
        PerformBackOnce();

    private void OnBackAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        PerformBackOnce();
    }

    /// <summary>
    /// Takes at most one back step per input, whether the toolkit raises the request or the
    /// accelerator does. The flag is cleared on the next dispatcher turn rather than
    /// immediately, so the second of two notifications for the same gesture is ignored.
    /// </summary>
    private void PerformBackOnce()
    {
        if (_backHandled)
        {
            return;
        }

        _backHandled = true;
        ViewModel.GoBackCommand.Execute(null);

        _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () => _backHandled = false);
    }

    private void OnPaneToggleClick(object sender, RoutedEventArgs args) =>
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
}
