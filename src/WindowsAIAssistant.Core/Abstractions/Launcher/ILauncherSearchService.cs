using WindowsAIAssistant.Core.Models.Launcher;

namespace WindowsAIAssistant.Core.Abstractions.Launcher;

/// <summary>
/// Routes a launcher query, gathers its results, and adds the actions the interface offers.
/// </summary>
public interface ILauncherSearchService
{
    /// <summary>Searches for a launcher request.</summary>
    Task<LauncherResult> SearchAsync(
        LauncherRequest request,
        CancellationToken cancellationToken = default);
}
