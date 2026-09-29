namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// The top-level destinations of the application shell.
/// <para>
/// A route is a name for a destination, not a page and not a string address. Nothing in the
/// application is allowed to pass <c>"/settings"</c> around: callers ask for
/// <see cref="Settings"/> and the navigation layer decides which page that means. That keeps a
/// route rename from breaking a voice phrase, a quick action, and a sidebar item at the same
/// time, and it leaves room for a later deep-link layer to translate a URI into a route rather
/// than into a page type.
/// </para>
/// <para>
/// The values are ordered as the sidebar presents them, so an ordinal value is a stable,
/// human-meaningful ordering rather than an accident of declaration.
/// </para>
/// </summary>
public enum NavigationRoute
{
    /// <summary>The landing page with the quick actions and the voice prompt.</summary>
    Home,

    /// <summary>The conversation page.</summary>
    Chat,

    /// <summary>The file search page.</summary>
    Files,

    /// <summary>
    /// One document being read, summarized, or asked about. A destination of its own rather
    /// than part of <see cref="Files"/> because the work happens against a single file that the
    /// person chose, which is a different kind of task from finding files by name.
    /// </summary>
    Document,

    /// <summary>
    /// The personal knowledge base: a list of indexed documents, and a place to ask questions
    /// across all of them. A destination of its own rather than part of <see cref="Files"/>,
    /// because the work is not finding a file by name but keeping documents indexed so a question
    /// can be answered from all of them at once.
    /// </summary>
    Knowledge,

    /// <summary>The saved automation page.</summary>
    Automations,

    /// <summary>
    /// This application's own settings page. It is deliberately distinct from the Windows
    /// Settings application, which the assistant opens through a separate intent.
    /// </summary>
    Settings
}
