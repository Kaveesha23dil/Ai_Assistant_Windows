using WindowsAIAssistant.Core.Models.Launcher;

namespace WindowsAIAssistant.Core.Abstractions.Launcher;

/// <summary>
/// Turns launcher text into a deterministic request kind before any AI is involved.
/// </summary>
public interface ILauncherIntentService
{
    /// <summary>Routes a query.</summary>
    LauncherIntent Route(string text);
}
