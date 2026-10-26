using WindowsAIAssistant.Core.Abstractions.Launcher;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Launcher;

namespace WindowsAIAssistant.Infrastructure.Launcher.SearchProviders;

/// <summary>
/// Suggests installed applications by display name.
/// </summary>
public sealed class ApplicationSearchProvider : IUniversalSearchProvider
{
    private readonly IApplicationCatalog _catalog;

    public ApplicationSearchProvider(IApplicationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <inheritdoc />
    public string Name => "Applications";

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

        var applications = await _catalog
            .GetInstalledApplicationsAsync(cancellationToken)
            .ConfigureAwait(false);

        var trimmed = query.Trim();
        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return applications
            .Select(application => (Application: application, Score: Score(trimmed, words, application.Name)))
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Application.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(maximumResults)
            .Select(candidate => new SearchResult
            {
                Id = $"application:{candidate.Application.Target}",
                Title = candidate.Application.Name,
                Description = "Application",
                Type = SearchResultType.Application,
                Group = "Applications",
                Score = candidate.Score,
                Glyph = "\uE71D",
                Action = LauncherAction.For(LauncherActionKind.LaunchApplication, candidate.Application.Name),
            })
            .ToArray();
    }

    private static double Score(string query, IReadOnlyList<string> words, string name)
    {
        if (name.Equals(query, StringComparison.CurrentCultureIgnoreCase))
        {
            return 1;
        }

        if (name.StartsWith(query, StringComparison.CurrentCultureIgnoreCase))
        {
            return 0.9;
        }

        if (name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
        {
            return 0.75;
        }

        var matchedWords = words.Count(word =>
            name.Contains(word, StringComparison.CurrentCultureIgnoreCase));

        return matchedWords == words.Count ? 0.55 : 0;
    }
}
