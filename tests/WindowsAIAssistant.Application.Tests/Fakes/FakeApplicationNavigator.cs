using WindowsAIAssistant.Core.Abstractions.Navigation;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Application.Tests.Fakes;

/// <summary>
/// Records navigation requests instead of showing a page.
/// <para>
/// The real navigator drives a WinUI frame, which cannot exist in a unit test. What matters
/// here is the decision the voice handler makes: which route a phrase resolves to, whether the
/// destination is offered at all, and whether the request was actually made. This fake answers
/// those questions without a window, which is why the voice navigation tests stay fast and
/// headless.
/// </para>
/// <para>
/// It also records the requests it was asked to refuse, so a test can prove that a phrase
/// belonging to Windows Settings never reaches the assistant's own settings page.
/// </para>
/// </summary>
public sealed class FakeApplicationNavigator : IApplicationNavigator
{
    /// <summary>Gets the routes the host declares it can show. Empty means nothing is available.</summary>
    public HashSet<NavigationRoute> AvailableRoutes { get; } =
    [
        NavigationRoute.Home,
        NavigationRoute.Chat,
        NavigationRoute.Files,
        NavigationRoute.Automations,
        NavigationRoute.Settings
    ];

    /// <summary>Gets every route the pipeline asked to show, in order.</summary>
    public List<NavigationRoute> Requests { get; } = [];

    /// <summary>Gets the value supplied with the most recent request, if any.</summary>
    public List<object?> Parameters { get; } = [];

    public NavigationRoute? CurrentRoute { get; set; }

    public bool CanGoBack { get; set; }

    /// <summary>Gets or sets a value indicating whether the next request is refused.</summary>
    public bool RefuseRequests { get; set; }

    /// <summary>Gets or sets a value indicating whether a request throws.</summary>
    public bool ThrowOnNavigate { get; set; }

    public bool CanNavigateTo(NavigationRoute route) => AvailableRoutes.Contains(route);

    public bool Navigate(NavigationRoute route, object? parameter = null)
    {
        Requests.Add(route);
        Parameters.Add(parameter);

        if (ThrowOnNavigate)
        {
            throw new InvalidOperationException("The frame is unavailable.");
        }

        if (RefuseRequests || !CanNavigateTo(route))
        {
            return false;
        }

        CurrentRoute = route;
        return true;
    }

    public bool GoBack()
    {
        if (!CanGoBack)
        {
            return false;
        }

        CanGoBack = false;
        CurrentRoute = null;
        return true;
    }
}
