using WindowsAIAssistant.Core.Models.Launcher;

namespace WindowsAIAssistant.Core.Abstractions.Launcher;

/// <summary>
/// Queries every local search provider and returns one ranked list.
/// </summary>
public interface IUniversalSearchService
{
    /// <summary>Searches every provider in parallel.</summary>
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int maximumResults,
        CancellationToken cancellationToken = default);
}
