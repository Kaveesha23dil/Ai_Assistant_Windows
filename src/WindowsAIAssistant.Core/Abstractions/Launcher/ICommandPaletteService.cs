using WindowsAIAssistant.Core.Models.Launcher;

namespace WindowsAIAssistant.Core.Abstractions.Launcher;

/// <summary>
/// Supplies the commands and destinations the launcher can offer.
/// </summary>
public interface ICommandPaletteService
{
    /// <summary>Finds commands matching a query. An empty query returns the default commands.</summary>
    IReadOnlyList<SearchResult> Search(string query, int maximumResults);
}
