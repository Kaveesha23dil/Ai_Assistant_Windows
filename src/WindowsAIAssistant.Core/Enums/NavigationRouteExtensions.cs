namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// Display names and safe parsing for <see cref="NavigationRoute"/>.
/// <para>
/// Presentation text lives here rather than in the pages, the sidebar, or the voice responses
/// so that "Settings" is spelled the same way everywhere and so that routing decisions never
/// depend on a label being matched. The parser is shared by the shell, which reads configured
/// and tag-supplied names, and by the voice handler, which reads a spoken destination, which
/// is what guarantees the two accept exactly the same vocabulary.
/// </para>
/// </summary>
public static class NavigationRouteExtensions
{
    /// <summary>The route the application opens when nothing else is configured.</summary>
    public const NavigationRoute Default = NavigationRoute.Home;

    /// <summary>
    /// Gets the name to show a person for a route.
    /// </summary>
    /// <remarks>
    /// This is the only place display text is produced. A route that is not a defined value
    /// falls back to the application-neutral name rather than throwing, so a corrupt
    /// configuration value can never take the shell down.
    /// </remarks>
    public static string ToDisplayName(this NavigationRoute route) => route switch
    {
        NavigationRoute.Home => "Home",
        NavigationRoute.Chat => "Chat",
        NavigationRoute.Files => "Files",
        NavigationRoute.Knowledge => "Knowledge",
        NavigationRoute.Automations => "Automations",
        NavigationRoute.Settings => "Settings",
        _ => "Home"
    };

    /// <summary>
    /// Attempts to read a route from text supplied by configuration, a navigation item tag, or
    /// a spoken phrase.
    /// </summary>
    /// <remarks>
    /// The accepted spellings are deliberately narrow. Unrecognised text is refused rather
    /// than guessed at, because a wrong guess here would send someone to a page they did not
    /// ask for, and <c>"settings"</c> on its own is accepted only when the caller already
    /// knows the request is about this application.
    /// </remarks>
    /// <param name="value">The text to interpret. May be <see langword="null"/>.</param>
    /// <param name="route">The parsed route, or the default when parsing failed.</param>
    /// <returns><see langword="true"/> when the text named a known destination.</returns>
    public static bool TryParse(string? value, out NavigationRoute route)
    {
        route = Default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = Normalize(value);

        switch (normalized)
        {
            case "home":
            case "start":
            case "main":
                route = NavigationRoute.Home;
                return true;

            case "chat":
            case "conversation":
            case "conversations":
                route = NavigationRoute.Chat;
                return true;

            case "file":
            case "files":
            case "my files":
            case "documents":
                route = NavigationRoute.Files;
                return true;

            case "automation":
            case "automations":
            case "routine":
            case "routines":
                route = NavigationRoute.Automations;
                return true;

            case "knowledge":
            case "knowledge base":
            case "my documents":
            case "indexed documents":
                route = NavigationRoute.Knowledge;
                return true;

            // "Assistant settings" and "app settings" are the ways a person distinguishes this
            // page from the Windows Settings application. The bare word is accepted too, but
            // only because a caller that reaches this method has already established that the
            // request is about the assistant rather than about Windows.
            case "setting":
            case "settings":
            case "app settings":
            case "application settings":
            case "assistant settings":
                route = NavigationRoute.Settings;
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Reduces a phrase to a comparable key: lower case, single spaces, and no leading or
    /// trailing punctuation left over from a spoken command.
    /// </summary>
    private static string Normalize(string value)
    {
        var trimmed = value.Trim().ToLowerInvariant();
        var builder = new System.Text.StringBuilder(trimmed.Length);

        var previousWasSpace = false;
        foreach (var character in trimmed)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                }

                previousWasSpace = true;
                continue;
            }

            builder.Append(character);
            previousWasSpace = false;
        }

        var result = builder.ToString().Trim();
        return result.EndsWith('.') ? result[..^1].TrimEnd() : result;
    }
}
