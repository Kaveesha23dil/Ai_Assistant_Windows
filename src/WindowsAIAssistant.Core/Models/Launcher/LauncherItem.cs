using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Launcher;

/// <summary>
/// A search result that is ready for the launcher interface to present and activate.
/// </summary>
public sealed record LauncherItem
{
    /// <summary>Gets the stable identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the primary text.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the secondary text.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Gets the result category.</summary>
    public required SearchResultType Type { get; init; }

    /// <summary>Gets the heading the result belongs under.</summary>
    public string Group { get; init; } = string.Empty;

    /// <summary>Gets the ranking score.</summary>
    public double Score { get; init; }

    /// <summary>Gets the icon glyph.</summary>
    public string Glyph { get; init; } = string.Empty;

    /// <summary>Gets the action to perform.</summary>
    public LauncherAction Action { get; init; } = LauncherAction.None;

    /// <summary>Projects a provider result onto a presentable item.</summary>
    public static LauncherItem FromSearchResult(SearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new LauncherItem
        {
            Id = result.Id,
            Title = result.Title,
            Description = result.Description,
            Type = result.Type,
            Group = result.Group,
            Score = result.Score,
            Glyph = result.Glyph,
            Action = result.Action,
        };
    }
}
