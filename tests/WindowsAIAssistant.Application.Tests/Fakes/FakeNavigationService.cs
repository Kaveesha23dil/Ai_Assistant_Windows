using Microsoft.UI.Xaml.Controls;
using WindowsAIAssistant.App.Services.Navigation;
using WindowsAIAssistant.Core.Abstractions.Navigation;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Application.Tests.Fakes;

/// <summary>
/// A stand-in for the shell's navigator: records routes and parameters instead of driving a
/// WinUI frame.
/// <para>
/// The frame cannot exist in a test, and no test here needs one — what a view model decides is
/// which route it asks for and what it hands over. The members that would touch a frame report
/// that they are unavailable rather than throwing, so a test that reached one would fail with
/// the reason rather than with a null reference.
/// </para>
/// </summary>
public sealed class FakeNavigationService : INavigationService
{
    /// <summary>Gets the routes the host declares it can show.</summary>
    public HashSet<NavigationRoute> AvailableRoutes { get; } =
    [
        NavigationRoute.Home,
        NavigationRoute.Chat,
        NavigationRoute.Files,
        NavigationRoute.Automations,
        NavigationRoute.Settings,
    ];

    public List<NavigationRoute> Requests { get; } = [];

    public List<object?> Parameters { get; } = [];

    public NavigationRoute? CurrentRoute { get; private set; }

    public NavigationRoute DefaultRoute => NavigationRoute.Home;

    public object? CurrentParameter { get; private set; }

    public bool CanGoBack { get; set; }

    public bool IsInitialized { get; private set; }

    public event EventHandler<NavigationEventArgs>? Navigated;

    public bool CanNavigateTo(NavigationRoute route) => AvailableRoutes.Contains(route);

    public bool Navigate(NavigationRoute route, object? parameter = null)
    {
        Requests.Add(route);
        Parameters.Add(parameter);

        if (!AvailableRoutes.Contains(route))
        {
            return false;
        }

        var previous = CurrentRoute;
        CurrentRoute = route;
        CurrentParameter = parameter;
        Navigated?.Invoke(this, new NavigationEventArgs(previous, route, parameter));
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
        CurrentParameter = null;
        return true;
    }

    public void Initialize(Frame frame) => IsInitialized = true;
}
