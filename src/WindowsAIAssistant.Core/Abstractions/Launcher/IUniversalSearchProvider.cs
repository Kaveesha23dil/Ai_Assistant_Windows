using WindowsAIAssistant.Core.Models.Launcher;

namespace WindowsAIAssistant.Core.Abstractions.Launcher;

/// <summary>
/// One local source of universal launcher results.
/// </summary>
public interface IUniversalSearchProvider
{
    /// <summary>Gets a stable name used in logs and tests.</summary>
    string Name { get; }

    /// <summary>Searches this provider.</summary>
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int maximumResults,
        CancellationToken cancellationToken = default);
}
