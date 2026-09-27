using Microsoft.UI.Xaml.Controls;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.App.Services.Navigation;

/// <summary>
/// One entry in the route-to-page table.
/// <para>
/// A registration pairs a <see cref="NavigationRoute"/> with the page that shows it. The pair
/// is declared once, in the registry, and is the only place in the application where a route
/// is associated with a page type. Callers ask for a route; nothing else needs to know what
/// type stands behind it.
/// </para>
/// <param name="Route">The destination the person asked for.</param>
/// <param name="PageType">The page that shows it. Must be a <see cref="Page"/>.</param>
public sealed record NavigationRegistration(NavigationRoute Route, Type PageType);
