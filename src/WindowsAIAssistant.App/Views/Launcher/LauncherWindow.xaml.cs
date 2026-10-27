using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WindowsAIAssistant.App.Controls;
using WindowsAIAssistant.App.ViewModels.Launcher;
using WindowsAIAssistant.Core.Abstractions.Launcher;
using WindowsAIAssistant.Infrastructure.Configuration.Options;
using Windows.Graphics;
using WinRT.Interop;

namespace WindowsAIAssistant.App.Views.Launcher;

/// <summary>
/// The borderless, always-on-top window the shortcut opens. It owns focus and keyboard handling;
/// whether the launcher should be visible at all is decided by the headless launcher service.
/// </summary>
public sealed partial class LauncherWindow : Window
{
    private const int DefaultWidth = 760;
    private const int DefaultHeight = 480;

    private readonly IAssistantLauncherService _launcher;
    private readonly IOptionsMonitor<UIOptions> _uiOptions;
    private readonly ILogger<LauncherWindow> _logger;
    private readonly IDisposable? _themeSubscription;
    private bool _allowClose;

    public LauncherWindow(
        LauncherViewModel viewModel,
        IAssistantLauncherService launcher,
        IOptionsMonitor<UIOptions> uiOptions,
        ILogger<LauncherWindow> logger)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(uiOptions);
        ArgumentNullException.ThrowIfNull(logger);

        ViewModel = viewModel;
        _launcher = launcher;
        _uiOptions = uiOptions;
        _logger = logger;

        InitializeComponent();


        ConfigureWindow();
        ApplyTheme();
        _themeSubscription = uiOptions.OnChange(_ => DispatcherQueue.TryEnqueue(ApplyTheme));

        Activated += OnActivated;
        Closed += OnClosed;
    }

    public LauncherViewModel ViewModel { get; }

    /// <summary>Gets the native window handle, for positioning and focus restoration.</summary>
    public nint Handle
    {
        get
        {
            try
            {
                return WindowNative.GetWindowHandle(this);
            }
            catch
            {
                return 0;
            }
        }
    }

    /// <summary>Resets the query and puts the caret in the search box.</summary>
    public void PrepareForShow()
    {
        ApplyTheme();
        ViewModel.PrepareForShow();

        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () =>
            {
                QueryBox.Focus(FocusState.Programmatic);
                QueryBox.SelectAll();
            });
    }

    /// <summary>Allows the host to close the window during application shutdown.</summary>
    public void AllowClose() => _allowClose = true;

    /// <summary>Gets whether the host may close this window instead of hiding it.</summary>
    public bool CanClose => _allowClose;

    private void ConfigureWindow()
    {
        try
        {
            ExtendsContentIntoTitleBar = true;

            var handle = WindowNative.GetWindowHandle(this);
            var appWindow = AppWindow.GetFromWindowId(
                Win32Interop.GetWindowIdFromWindow(handle));

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.IsAlwaysOnTop = true;
            }

            appWindow.IsShownInSwitchers = false;
            appWindow.Resize(new SizeInt32(DefaultWidth, DefaultHeight));

            SystemBackdrop = new DesktopAcrylicBackdrop();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "The launcher window chrome could not be customized.");
        }
    }

    private void ApplyTheme()
    {
        RootGrid.RequestedTheme = ThemeHelper.Resolve(_uiOptions.CurrentValue.Theme);
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            _ = _launcher.HideAsync();
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Activated -= OnActivated;
        _themeSubscription?.Dispose();
    }

    private void OnQueryKeyDown(object sender, KeyRoutedEventArgs args)
    {
        switch (args.Key)
        {
            case Windows.System.VirtualKey.Enter:
                args.Handled = true;
                _ = ViewModel.ExecuteSelectedAsync();
                break;

            case Windows.System.VirtualKey.Down:
                args.Handled = true;
                ViewModel.MoveSelection(1);
                break;

            case Windows.System.VirtualKey.Up:
                args.Handled = true;
                ViewModel.MoveSelection(-1);
                break;

            case Windows.System.VirtualKey.Escape:
                args.Handled = true;
                _ = _launcher.HideAsync();
                break;
        }
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Handled)
        {
            return;
        }

        if (args.Key == Windows.System.VirtualKey.Escape)
        {
            args.Handled = true;
            _ = _launcher.HideAsync();
        }
        else if (args.Key == Windows.System.VirtualKey.Enter)
        {
            args.Handled = true;
            _ = ViewModel.ExecuteSelectedAsync();
        }
    }

    private async void OnResultClick(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is not LauncherItemViewModel item)
        {
            return;
        }

        ViewModel.SelectedItem = item;
        await ViewModel.ExecuteSelectedAsync();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        // Selection is bound to the view model. This handler exists so the list owns keyboard
        // selection changes made by the platform as well as by the arrow keys above.
    }
}
