using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Navigation;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Application.Navigation;

/// <summary>
/// The navigator a host registers when it has no user interface to navigate.
/// <para>
/// The voice pipeline is built in contexts that have no window at all, and a voice command that
/// depends on navigation must still be routable there or the whole container would fail to
/// resolve. This implementation answers honestly instead: it has no pages, so it reports that
/// the requested destination is unavailable and refuses to move. A spoken request therefore
/// gets "that page isn't available yet" rather than silence, and the failure is visible in a
/// test rather than hidden behind an implementation that claims to have succeeded.
/// </para>
/// <para>
/// A host that does have a shell, such as the WinUI application, registers its own navigator
/// afterwards and this one is never consulted.
/// </para>
/// </summary>
public sealed class NullApplicationNavigator(ILogger<NullApplicationNavigator> logger) : IApplicationNavigator
{
    /// <inheritdoc />
    /// <remarks>Always <see langword="null"/>, because nothing is ever displayed.</remarks>
    public NavigationRoute? CurrentRoute => null;

    /// <inheritdoc />
    /// <remarks>Always <see langword="false"/>, because there is no history to return through.</remarks>
    public bool CanGoBack => false;

    /// <inheritdoc />
    public bool CanNavigateTo(NavigationRoute route) => false;

    /// <inheritdoc />
    public bool Navigate(NavigationRoute route, object? parameter = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        logger.LogInformation(
            "Navigation to {Route} was refused because this host has no user interface to navigate.",
            route);

        return false;
    }

    /// <inheritdoc />
    public bool GoBack() => false;
}
