using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Core.Abstractions.Files;

/// <summary>
/// Searches the local file system for files matching a query.
/// </summary>
public interface IFileSearchService
{
    /// <summary>Performs a file search for the specified query.</summary>
    Task<IReadOnlyCollection<FileSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default);
}