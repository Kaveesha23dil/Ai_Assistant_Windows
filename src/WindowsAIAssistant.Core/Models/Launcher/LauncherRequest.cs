namespace WindowsAIAssistant.Core.Models.Launcher;

/// <summary>
/// A request to search from the launcher.
/// </summary>
public sealed record LauncherRequest
{
    /// <summary>Gets the text typed into the launcher.</summary>
    public required string Text { get; init; }

    /// <summary>Gets the greatest number of results to return.</summary>
    public int MaximumResults { get; init; } = 20;
}
