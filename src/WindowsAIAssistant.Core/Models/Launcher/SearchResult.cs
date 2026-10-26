using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Launcher;

/// <summary>
/// One result produced by a universal search provider.
/// </summary>
public sealed record SearchResult
{
    /// <summary>Gets a stable identifier used to remove duplicates between providers.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the primary text shown for the result.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the secondary text shown under the title.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Gets the result category.</summary>
    public required SearchResultType Type { get; init; }

    /// <summary>Gets the heading the result belongs under.</summary>
    public string Group { get; init; } = string.Empty;

    /// <summary>Gets the ranking score, where larger is more relevant.</summary>
    public double Score { get; init; }

    /// <summary>Gets the Segoe Fluent glyph shown beside the result.</summary>
    public string Glyph { get; init; } = string.Empty;

    /// <summary>Gets what choosing the result does.</summary>
    public LauncherAction Action { get; init; } = LauncherAction.None;
}
