using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.App.Services.Navigation;

/// <summary>
/// Resolves routes to pages and shows them in the shell's frame.
/// <para>
/// The service is a singleton and is therefore shared, but every operation that touches the
/// frame is marshalled onto the thread that owns it. That matters because the voice pipeline
/// runs off the user interface thread: a spoken "open chat" arrives on a worker, and touching a
/// frame from there would throw. Requests made from a background thread are queued onto the
/// dispatcher rather than run inline, so the call returns as soon as the request has been
/// accepted and <see cref="INavigationService.Navigated"/> reports the outcome.
/// </para>
/// <para>
/// The service owns the history. A frame's own back stack is the obvious alternative, but a
/// frame builds the page itself when it is given a type, which would force every page to have a
/// parameterless constructor and to reach for its own dependencies. That is the arrangement this
/// application is avoiding, and the price of avoiding it is that the route history lives here
/// rather than in the toolkit. Because this is the only thing that writes to the frame, there is
/// still exactly one route-to-page mapping and one history to reason about.
/// </para>
/// <para>
/// What it deliberately does not do is hold any business rule. It does not call an AI service,
/// search for files, read the clipboard, or run a system command. Those belong to the layers
/// that own them, and the service would only obscure them.
/// </para>
/// </summary>
public sealed class NavigationService : INavigationService
{
    private readonly IServiceProvider _services;
    private readonly NavigationRouteRegistry _registry;
    private readonly ILogger<NavigationService> _logger;
    private readonly List<HistoryEntry> _history = [];
    private readonly string _configuredDefault;

    private Frame? _frame;
    private DispatcherQueue? _dispatcher;
    private NavigationRoute? _currentRoute;
    private object? _currentParameter;

    public NavigationService(
        IServiceProvider services,
        NavigationRouteRegistry registry,
        IOptions<UIOptions> uiOptions,
        ILogger<NavigationService> logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(uiOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _services = services;
        _registry = registry;
        _logger = logger;
        _configuredDefault = uiOptions.Value.DefaultPage;

        DefaultRoute = _registry.ResolveDefault(_configuredDefault);

        // The warning is for a default that could not be used, not for one that was spelled
        // differently. "assistant settings" is a valid name for a page that exists, and warning
        // about it would train someone to ignore the warning.
        if (!NavigationRouteExtensions.TryParse(_configuredDefault, out var configured)
            || configured != DefaultRoute)
        {
            _logger.LogWarning(
                "The configured default page '{ConfiguredDefault}' is not a known destination. Falling back to {Fallback}.",
                _configuredDefault,
                DefaultRoute);
        }
    }

    /// <inheritdoc />
    public event EventHandler<NavigationEventArgs>? Navigated;

    /// <inheritdoc />
    public NavigationRoute DefaultRoute { get; }

    /// <inheritdoc />
    public NavigationRoute? CurrentRoute => _currentRoute;

    /// <inheritdoc />
    public bool CanGoBack => _frame is not null && _history.Count > 1;

    /// <inheritdoc />
    public object? CurrentParameter => _currentParameter;

    /// <inheritdoc />
    public void Initialize(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (ReferenceEquals(_frame, frame))
        {
            return;
        }

        _frame = frame;
        _dispatcher = frame.DispatcherQueue;

        _logger.LogInformation("Navigation was initialized with the shell frame.");
    }

    /// <inheritdoc />
    public bool CanNavigateTo(NavigationRoute route) => _registry.Contains(route);

    /// <inheritdoc />
    public bool Navigate(NavigationRoute route, object? parameter = null)
    {
        // An unknown route is refused rather than sent somewhere unrelated. Guessing would be
        // worse than doing nothing: the person would be shown a page they did not ask for and
        // would have no way to tell that the request had been misread.
        if (!_registry.Contains(route))
        {
            _logger.LogWarning(
                "Navigation was refused for {Route} because the route has no registered page.",
                route);
            return false;
        }

        return RunOnUserInterfaceThread(() => Show(route, parameter, isBackNavigation: false));
    }

    /// <inheritdoc />
    public bool GoBack()
    {
        if (_frame is null)
        {
            _logger.LogWarning("Back navigation was refused because navigation has not been initialized.");
            return false;
        }

        return RunOnUserInterfaceThread(() =>
        {
            if (_history.Count <= 1)
            {
                _logger.LogInformation("Back navigation was refused because there is no history.");
                return false;
            }

            // The entry that is on screen is dropped and the one beneath it is shown again. The
            // page instance is reused, so returning to a page restores what the user left on it
            // rather than rebuilding an empty one.
            _history.RemoveAt(_history.Count - 1);

            var target = _history[^1];

            try
            {
                _frame!.Content = target.Page;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Back navigation failed.");
                return false;
            }

            Announce(target.Route, target.Parameter, isBackNavigation: true);
            _logger.LogInformation("Back navigation completed.");
            return true;
        });
    }

    /// <summary>
    /// Builds the page for a route and puts it on screen. Only ever runs on the thread that owns
    /// the frame.
    /// </summary>
    private bool Show(NavigationRoute route, object? parameter, bool isBackNavigation)
    {
        if (_frame is not { } frame)
        {
            _logger.LogWarning(
                "Navigation to {Route} was refused because navigation has not been initialized.",
                route);
            return false;
        }

        // A second request for the page already on screen is accepted and then ignored, so the
        // history is not filled with copies of the same page. A request that carries a parameter
        // is the exception: it is asking for a specific conversation or a specific file rather
        // than for the page, so the current entry is refreshed rather than stacked on.
        if (_currentRoute == route)
        {
            if (parameter is null)
            {
                _logger.LogInformation("Navigation to {Route} was skipped because it is already showing.", route);
                return true;
            }

            _history[^1] = new HistoryEntry(route, parameter, frame.Content as Page);
        }
        else
        {
            _logger.LogInformation("Navigation requested: {Route}.", route);

            if (!_registry.TryResolve(route, out var pageType) || pageType is null)
            {
                _logger.LogWarning("Navigation to {Route} failed because the route could not be resolved.", route);
                return false;
            }

            Page? page;
            try
            {
                // The page and its ViewModel are built by the container, which is what keeps a
                // page from having to know how to assemble its own dependencies.
                page = _services.GetService(pageType) as Page;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Navigation to {Route} failed while the page was being created.", route);
                return false;
            }

            if (page is null)
            {
                _logger.LogError(
                    "Navigation to {Route} failed because {PageType} is not registered in the container.",
                    route,
                    pageType.Name);
                return false;
            }

            _history.Add(new HistoryEntry(route, parameter, page));

            try
            {
                frame.Content = page;
            }
            catch (Exception exception)
            {
                _history.RemoveAt(_history.Count - 1);
                _logger.LogError(exception, "Navigation to {Route} failed.", route);
                return false;
            }
        }

        Announce(route, parameter, isBackNavigation);
        return true;
    }

    /// <summary>
    /// Publishes the state that follows from a page change. Every entry point ends here, which
    /// is what guarantees the route, the parameter, and the event always describe the page that
    /// is actually on screen.
    /// </summary>
    private void Announce(NavigationRoute route, object? parameter, bool isBackNavigation)
    {
        var previous = _currentRoute;
        _currentRoute = route;
        _currentParameter = parameter;

        if (!isBackNavigation)
        {
            // The parameter is deliberately absent here: it can hold a conversation identifier
            // or a selected file, neither of which belongs in a log.
            _logger.LogInformation("Navigation completed: {Route}.", route);
        }

        Navigated?.Invoke(
            this,
            new NavigationEventArgs(previous, route, parameter)
            {
                IsBackNavigation = isBackNavigation
            });
    }

    /// <summary>
    /// Runs work on the thread that owns the frame, queueing it when the caller is elsewhere.
    /// <para>
    /// The return value is exact only when the work ran inline. A queued call reports that the
    /// request was accepted; whether the page then appeared is announced by
    /// <see cref="Navigated"/>, which is also what a background caller should listen to.
    /// </para>
    /// </summary>
    private bool RunOnUserInterfaceThread(Func<bool> work)
    {
        var dispatcher = _dispatcher;

        if (dispatcher is null || dispatcher.HasThreadAccess)
        {
            return work();
        }

        if (!dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                try
                {
                    work();
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "A queued navigation request failed.");
                }
            }))
        {
            _logger.LogWarning("A navigation request could not be marshalled to the user interface thread.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// One step in the history: what was shown, what it was shown with, and the instance that
    /// was put on screen. The instance is kept so that going back restores the page as the user
    /// left it instead of building a fresh one.
    /// </summary>
    private readonly record struct HistoryEntry(NavigationRoute Route, object? Parameter, Page? Page);
}
