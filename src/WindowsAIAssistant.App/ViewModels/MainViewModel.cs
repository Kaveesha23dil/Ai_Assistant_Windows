using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.App.Services.Navigation;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.App.ViewModels;

/// <summary>
/// Shell-level state for the main window.
/// <para>
/// The ViewModel holds the state the shell binds to and nothing else. It asks
/// <see cref="INavigationService"/> to move and then mirrors what the service reports, which
/// is what keeps a page opened by the sidebar, by a quick action, or by a spoken command
/// indistinguishable from one another: in every case the change originates in the service, and
/// the selection, the back availability, and the title all follow from the same notification.
/// </para>
/// <para>
/// It never calls into a frame. Doing so would put the one piece of navigation that cannot be
/// exercised without a window in the one place a test would want to reach, and it would give
/// the shell a second way to change page that the service could not observe.
/// </para>
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly INavigationService _navigation;
    private bool _disposed;

    public MainViewModel(
        INavigationService navigation,
        IOptions<ApplicationOptions> applicationOptions,
        IOptions<UIOptions> uiOptions)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(applicationOptions);
        ArgumentNullException.ThrowIfNull(uiOptions);

        _navigation = navigation;

        if (!string.IsNullOrWhiteSpace(applicationOptions.Value.Name))
        {
            ApplicationTitle = applicationOptions.Value.Name;
        }

        CurrentTheme = uiOptions.Value.Theme;

        // Seeded from the service rather than from configuration directly, so the fallback to
        // Home for an unusable configured value is decided in one place. The window replaces
        // this with the route that actually finished loading.
        SelectedRoute = navigation.DefaultRoute;

        _navigation.Navigated += OnNavigated;
    }

    /// <summary>Gets the product name shown in the navigation pane header.</summary>
    [ObservableProperty]
    public partial string ApplicationTitle { get; set; } = "Windows AI Assistant";

    /// <summary>
    /// Gets or sets the route the sidebar should highlight. It is assigned by the navigation
    /// service's event rather than by the sidebar handler, which is what makes the highlight
    /// follow a change made anywhere rather than only one made by a click.
    /// </summary>
    [ObservableProperty]
    public partial NavigationRoute? SelectedRoute { get; set; }

    /// <summary>Gets a value indicating whether the back button should be enabled.</summary>
    [ObservableProperty]
    public partial bool CanGoBack { get; private set; }

    /// <summary>Gets or sets a value indicating whether the navigation pane is expanded.</summary>
    [ObservableProperty]
    public partial bool IsNavigationPaneOpen { get; set; } = true;

    /// <summary>Gets the configured theme name, applied once when the window is created.</summary>
    public string CurrentTheme { get; }

    /// <summary>
    /// Gets the route the window opens with, resolved from configuration by the navigation
    /// service and falling back to Home when the configured value is not a real destination.
    /// </summary>
    public NavigationRoute InitialRoute => _navigation.DefaultRoute;

    /// <summary>Gets the name of the current route, used for the header and the window title.</summary>
    public string SectionTitle =>
        SelectedRoute?.ToDisplayName() ?? ApplicationTitle;

    /// <summary>Gets the window title, which follows the route.</summary>
    public string WindowTitle => $"{SectionTitle} — {ApplicationTitle}";

    /// <summary>
    /// Reports whether a destination can be shown, so a command bound to a menu entry can
    /// refuse an unavailable route instead of failing silently when it is invoked.
    /// </summary>
    public bool CanNavigateTo(NavigationRoute route) => _navigation.CanNavigateTo(route);

    /// <summary>Asks the navigation service to show a route.</summary>
    [RelayCommand]
    private void Navigate(NavigationRoute route) => _navigation.Navigate(route);

    /// <summary>Asks the navigation service to return to the previous page.</summary>
    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack() => _navigation.GoBack();

    partial void OnSelectedRouteChanged(NavigationRoute? value) =>
        OnPropertyChanged(nameof(SectionTitle));

    partial void OnApplicationTitleChanged(string value) =>
        OnPropertyChanged(nameof(WindowTitle));

    partial void OnCanGoBackChanged(bool value) => GoBackCommand.NotifyCanExecuteChanged();

    private void OnNavigated(object? sender, NavigationEventArgs e)
    {
        SelectedRoute = e.CurrentRoute;
        CanGoBack = _navigation.CanGoBack;

        OnPropertyChanged(nameof(WindowTitle));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _navigation.Navigated -= OnNavigated;

        GC.SuppressFinalize(this);
    }
}
