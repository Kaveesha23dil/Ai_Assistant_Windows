using WindowsAIAssistant.Core.Abstractions.Files;
using WindowsAIAssistant.Core.Abstractions.Launcher;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Launcher;

namespace WindowsAIAssistant.Infrastructure.Launcher.SearchProviders;

/// <summary>
/// Suggests files through the existing file-search service.
/// </summary>
public sealed class FileSearchProvider : IUniversalSearchProvider
{
    private readonly IFileSearchService _files;

    public FileSearchProvider(IFileSearchService files)
    {
        ArgumentNullException.ThrowIfNull(files);
        _files = files;
    }

    /// <inheritdoc />
    public string Name => "Files";

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int maximumResults,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || maximumResults < 1)
        {
            return [];
        }

        var results = await _files
            .SearchAsync(query.Trim(), cancellationToken)
            .ConfigureAwait(false);

        return results
            .OrderByDescending(result => result.RelevanceScore)
            .ThenByDescending(result => result.LastModified)
            .Take(maximumResults)
            .Select(result => new SearchResult
            {
                Id = $"file:{result.FullPath}",
                Title = result.Name,
                Description = result.FullPath,
                Type = SearchResultType.File,
                Group = "Files",
                Score = Math.Clamp(result.RelevanceScore, 0, 1),
                Glyph = "\uE8A5",
                Action = LauncherAction.For(LauncherActionKind.OpenFile, result.FullPath),
            })
            .ToArray();
    }
}
