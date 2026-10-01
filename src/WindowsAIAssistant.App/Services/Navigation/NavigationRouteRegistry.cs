using Microsoft.UI.Xaml.Controls;
using WindowsAIAssistant.App.Views.Pages;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.App.Services.Navigation;

/// <summary>
/// The single route-to-page table, in both directions.
/// <para>
/// Every route mapping in the application is declared here and nowhere else. Because the table
/// is a plain set of values built from <see cref="Type"/> objects, it is decided entirely
/// before any window exists, which is what lets the mapping be asserted in a unit test without
/// a frame, a page, or a running application.
/// </para>
/// <para>
/// Both directions are kept. Route to page answers "where does this request go", and page to
/// route answers "which sidebar item belongs to the page that just appeared". Keeping the
/// reverse index in step inside the same type removes the possibility of a page appearing
/// through some other path with no item to highlight.
/// </para>
/// </summary>
public sealed class NavigationRouteRegistry
{
    private readonly Dictionary<NavigationRoute, Type> _pagesByRoute;
    private readonly Dictionary<Type, NavigationRoute> _routesByPage;

    private NavigationRouteRegistry(IEnumerable<NavigationRegistration> registrations)
    {
        _pagesByRoute = new Dictionary<NavigationRoute, Type>();
        _routesByPage = new Dictionary<Type, NavigationRoute>();

        foreach (var registration in registrations)
        {
            if (registration.PageType is null || !typeof(Page).IsAssignableFrom(registration.PageType))
            {
                throw new ArgumentException(
                    $"The page registered for {registration.Route} must derive from Page.",
                    nameof(registrations));
            }

            if (!_pagesByRoute.TryAdd(registration.Route, registration.PageType))
            {
                throw new ArgumentException(
                    $"The route {registration.Route} is registered more than once.",
                    nameof(registrations));
            }

            if (!_routesByPage.TryAdd(registration.PageType, registration.Route))
            {
                throw new ArgumentException(
                    $"The page {registration.PageType.Name} is registered for more than one route.",
                    nameof(registrations));
            }
        }
    }

    /// <summary>Gets the routes that have a page.</summary>
    public IReadOnlyCollection<NavigationRoute> Routes => _pagesByRoute.Keys;

    /// <summary>Gets the pages in the order the sidebar presents them.</summary>
    public IReadOnlyCollection<NavigationRegistration> Registrations =>
        _pagesByRoute
            .OrderBy(pair => pair.Key)
            .Select(pair => new NavigationRegistration(pair.Key, pair.Value))
            .ToArray();

    /// <summary>
    /// Builds the production table.
    /// <para>
    /// This is the only declaration of what a route looks like on screen. Adding a page means
    /// adding a line here; it does not mean editing a switch in the shell, a branch in the
    /// voice layer, and a mapping in a sidebar.
    /// </para>
    /// </summary>
    public static NavigationRouteRegistry CreateDefault() => new(DefaultRegistrations);

    /// <summary>Builds a table from an explicit set of registrations.</summary>
    public static NavigationRouteRegistry Create(IEnumerable<NavigationRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        return new NavigationRouteRegistry(registrations);
    }

    /// <summary>Builds a table with no entries, used to prove how unknown routes are handled.</summary>
    public static NavigationRouteRegistry CreateEmpty() =>
        new(Array.Empty<NavigationRegistration>());

    /// <summary>Reports whether a route has a page.</summary>
    public bool Contains(NavigationRoute route) => _pagesByRoute.ContainsKey(route);

    /// <summary>Finds the page registered for a route.</summary>
    public bool TryResolve(NavigationRoute route, out Type? pageType) =>
        _pagesByRoute.TryGetValue(route, out pageType);

    /// <summary>Finds the route a page belongs to.</summary>
    public bool TryResolveRoute(Type? pageType, out NavigationRoute route)
    {
        if (pageType is not null && _routesByPage.TryGetValue(pageType, out var resolved))
        {
            route = resolved;
            return true;
        }

        route = NavigationRouteExtensions.Default;
        return false;
    }

    /// <summary>
    /// Decides which route the application should open with.
    /// <para>
    /// A configured default is honoured only when it names a real, registered route. Anything
    /// else falls back to Home, so a typo in configuration produces a working application
    /// rather than a blank one. The decision is kept free of logging and of any window, so it
    /// is a pure function of the table and the configured text.
    /// </para>
    /// </summary>
    /// <param name="configuredDefault">The value read from configuration, if any.</param>
    /// <returns>The route to open with. Never a value that has no page.</returns>
    public NavigationRoute ResolveDefault(string? configuredDefault)
    {
        if (NavigationRouteExtensions.TryParse(configuredDefault, out var configured)
            && _pagesByRoute.ContainsKey(configured))
        {
            return configured;
        }

        if (_pagesByRoute.ContainsKey(NavigationRouteExtensions.Default))
        {
            return NavigationRouteExtensions.Default;
        }

        // Home is not registered, which the shipped table never does. Falling back to the first
        // registered route keeps the contract, that the result is always showable, even for a
        // table assembled by a test or a future host.
        return _pagesByRoute.Keys
            .OrderBy(route => route)
            .FirstOrDefault();
    }

    /// <summary>
    /// The complete route-to-page mapping. Adding a destination to the shell means adding an
    /// entry here; nothing else in the application needs to know the page exists.
    /// </summary>
    private static IEnumerable<NavigationRegistration> DefaultRegistrations =>
    [
        new NavigationRegistration(NavigationRoute.Home, typeof(HomePage)),
        new NavigationRegistration(NavigationRoute.Chat, typeof(ChatPage)),
        new NavigationRegistration(NavigationRoute.Files, typeof(FilesPage)),
        new NavigationRegistration(NavigationRoute.Document, typeof(DocumentPage)),
        new NavigationRegistration(NavigationRoute.Knowledge, typeof(KnowledgePage)),
        new NavigationRegistration(NavigationRoute.Automations, typeof(AutomationsPage)),
        new NavigationRegistration(NavigationRoute.Agent, typeof(AgentWorkspacePage)),
        new NavigationRegistration(NavigationRoute.Settings, typeof(SettingsPage))
    ];
}
