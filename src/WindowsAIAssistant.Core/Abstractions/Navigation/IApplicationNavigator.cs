using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Abstractions.Navigation;

/// <summary>
/// The way to move between this application's own pages, expressed without any reference to a
/// user interface toolkit.
/// <para>
/// The interface lives in Core so that a capability which needs to change page, such as the
/// voice assistant, can ask for it without knowing that pages live in a WinUI frame, and so
/// that the shell remains free to satisfy it however it likes. The voice layer therefore
/// speaks in routes and never touches a frame, and a host with no window at all can still
/// resolve the whole pipeline.
/// </para>
/// <para>
/// Implementations are expected to be called on, or to marshal onto, the thread that owns the
/// visual tree. Callers must therefore be prepared for a request made from a background thread
/// to be accepted rather than already applied.
/// </para>
/// </summary>
public interface IApplicationNavigator
{
    /// <summary>
    /// Gets the route currently on screen, or <see langword="null"/> when no registered page
    /// has been shown yet. It is <see langword="null"/> rather than a default value on
    /// purpose: "nothing is displayed" and "Home is displayed" are different states, and
    /// collapsing them would hide a startup failure.
    /// </summary>
    NavigationRoute? CurrentRoute { get; }

    /// <summary>Gets a value indicating whether there is somewhere to go back to.</summary>
    bool CanGoBack { get; }

    /// <summary>
    /// Reports whether a route can be shown, which is false for a route that has no registered
    /// page. It makes no change and is safe to call from a bound command's predicate.
    /// </summary>
    bool CanNavigateTo(NavigationRoute route);

    /// <summary>
    /// Shows the page registered for a route.
    /// </summary>
    /// <param name="route">The destination.</param>
    /// <param name="parameter">
    /// An optional in-process value handed to the destination. It is delivered to the page
    /// through the navigation event rather than serialized, so a value never leaves the
    /// process and never becomes part of an address.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the request was accepted, which includes a request that was
    /// a no-op because the route was already on screen, and <see langword="false"/> when the
    /// route is unknown or the page could not be shown.
    /// </returns>
    bool Navigate(NavigationRoute route, object? parameter = null);

    /// <summary>Returns to the previous page.</summary>
    /// <returns>
    /// <see langword="true"/> when there was somewhere to go and the move happened, otherwise
    /// <see langword="false"/>. A refusal is an ordinary outcome, not an error.
    /// </returns>
    bool GoBack();
}
