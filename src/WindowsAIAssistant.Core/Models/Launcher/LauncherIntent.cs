using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Launcher;

/// <summary>
/// The meaning the launcher inferred from the text in the search box.
/// </summary>
public sealed record LauncherIntent
{
    /// <summary>Gets the kind of request.</summary>
    public LauncherIntentType Type { get; init; } = LauncherIntentType.Universal;

    /// <summary>Gets the query with any routing prefix removed.</summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>Gets how certain the router is, from 0 to 1.</summary>
    public double Confidence { get; init; }

    /// <summary>Gets a value indicating whether the intent needs the conversational assistant.</summary>
    public bool RequiresAi => Type is LauncherIntentType.AiQuestion;

    /// <summary>Creates a universal intent.</summary>
    public static LauncherIntent Universal(string query) =>
        new() { Type = LauncherIntentType.Universal, Query = query, Confidence = 0.5 };

    /// <summary>Creates an intent of the given kind.</summary>
    public static LauncherIntent For(LauncherIntentType type, string query, double confidence) =>
        new() { Type = type, Query = query, Confidence = confidence };
}
