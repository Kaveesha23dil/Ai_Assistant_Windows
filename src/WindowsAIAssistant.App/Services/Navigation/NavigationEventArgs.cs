using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.App.Services.Navigation;

/// <summary>
/// Data published after a page change has actually taken effect.
/// <para>
/// The event exists so that presentation concerns which are not routing concerns can observe
/// navigation without reaching into the frame: the sidebar highlight, the window title, a
/// breadcrumb, a voice acknowledgement, or an accessibility announcement. Analytics is the
/// obvious future use and is deliberately not implemented here.
/// </para>
/// <para>
/// The event is raised only for a route that genuinely changed. A request that was a no-op
/// because the route was already on screen raises nothing, so a listener can treat every
/// notification as a real page change.
/// </para>
/// </summary>
public sealed class NavigationEventArgs : EventArgs
{
    /// <summary>Creates the arguments describing a completed page change.</summary>
    /// <param name="previousRoute">The route that was on screen, or <see langword="null"/> at startup.</param>
    /// <param name="currentRoute">The route that is on screen now.</param>
    /// <param name="parameter">The value handed to the destination, if any.</param>
    public NavigationEventArgs(
        NavigationRoute? previousRoute,
        NavigationRoute currentRoute,
        object? parameter = null)
    {
        PreviousRoute = previousRoute;
        CurrentRoute = currentRoute;
        Parameter = parameter;
    }

    /// <summary>Gets the route that was on screen beforehand.</summary>
    public NavigationRoute? PreviousRoute { get; }

    /// <summary>Gets the route that is on screen now.</summary>
    public NavigationRoute CurrentRoute { get; }

    /// <summary>
    /// Gets the value supplied by the caller, if there was one.
    /// <para>
    /// A parameter may be a conversation identifier, a selected file, or anything else the
    /// caller chose to pass, so it is treated as potentially sensitive: it is passed on to the
    /// destination but never written to a log or included in a message.
    /// </para>
    /// </summary>
    public object? Parameter { get; }

    /// <summary>
    /// Gets a value indicating whether the change was a return to the previous page rather
    /// than a fresh move. Listeners that track a route trail can use it instead of inferring
    /// the direction from the routes themselves.
    /// </summary>
    public bool IsBackNavigation { get; init; }
}
