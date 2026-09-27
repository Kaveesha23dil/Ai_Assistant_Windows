using Microsoft.UI.Xaml.Controls;
using WindowsAIAssistant.Core.Abstractions.Navigation;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.App.Services.Navigation;

/// <summary>
/// The shell's single navigation architecture.
/// <para>
/// Every entry point uses this one service: the sidebar, the shell ViewModel, a Home quick
/// action, a spoken command, and any future notification or deep link. Because they all funnel
/// through the same object, the selected sidebar item, the window title, the current route, and
/// whether the back button is enabled are guaranteed to describe the same page, no matter
/// which entry point caused the change.
/// </para>
/// <para>
/// The interface is UI-facing: it exposes the frame handshake that a WinUI shell needs, and
/// the <see cref="Navigated"/> event that the shell listens to. The navigation decisions
/// themselves are expressed by the toolkit-independent
/// <see cref="IApplicationNavigator"/> this interface extends, so the rest of the application
/// depends only on routes.
/// </para>
/// <para>
/// The service is registered as a singleton because it represents one shared piece of state:
/// where the application currently is. It holds no business logic, calls no AI service, and
/// touches no system setting. Its only job is to decide which page is shown and to keep the
/// state around that decision consistent.
/// </para>
/// </summary>
public interface INavigationService : IApplicationNavigator
{
    /// <summary>Raised after a page change has taken effect.</summary>
    event EventHandler<NavigationEventArgs>? Navigated;

    /// <summary>
    /// Gets the route the application opens with. It is the configured default when the
    /// configuration names a valid route, and Home otherwise.
    /// </summary>
    NavigationRoute DefaultRoute { get; }

    /// <summary>
    /// Gets the value handed to the page currently on screen, or <see langword="null"/>.
    /// <para>
    /// The parameter is delivered here and through <see cref="NavigationEventArgs"/> rather
    /// than by serializing it into a page address. That keeps it in the process, which is what
    /// lets a page be constructed by the container instead of by a parameterless constructor,
    /// and it means no parameter can leak into a URI or into a log.
    /// </para>
    /// </summary>
    object? CurrentParameter { get; }

    /// <summary>
    /// Hands the service the frame that hosts the pages.
    /// <para>
    /// This is called once by the window, which is the only place that owns a frame. The
    /// alternative, a static reference to the running window, would make the navigation
    /// decisions depend on a global and untestable. Every request made before this call is
    /// refused and logged rather than throwing, so an early request degrades instead of
    /// crashing.
    /// </para>
    /// </summary>
    void Initialize(Frame frame);}
