using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Launcher;
using WindowsAIAssistant.Core.Models.Launcher;

namespace WindowsAIAssistant.Application.Launcher;

/// <summary>
/// Fans a query out to independent local providers. One slow or failing provider never prevents
/// the others from returning, and every result is cancelled when the query changes.
/// </summary>
public sealed class UniversalSearchService : IUniversalSearchService
{
    private readonly IReadOnlyList<IUniversalSearchProvider> _providers;
    private readonly LauncherSearchPolicy _policy;
    private readonly ILogger<UniversalSearchService> _logger;

    public UniversalSearchService(
        IEnumerable<IUniversalSearchProvider> providers,
        LauncherSearchPolicy policy,
        ILogger<UniversalSearchService> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(logger);

        _providers = providers.ToArray();
        _policy = policy;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int maximumResults,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || maximumResults < 1 || _providers.Count == 0)
        {
            return [];
        }

        var tasks = _providers
            .Select(provider => SearchProviderAsync(
                provider,
                query.Trim(),
                maximumResults,
                _policy.ProviderTimeoutMilliseconds,
                cancellationToken))
            .ToArray();

        var providerResults = await Task.WhenAll(tasks).ConfigureAwait(false);

        return providerResults
            .SelectMany(results => results)
            .GroupBy(result => result.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(result => result.Score).First())
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(maximumResults)
            .ToArray();
    }

    private async Task<IReadOnlyList<SearchResult>> SearchProviderAsync(
        IUniversalSearchProvider provider,
        string query,
        int maximumResults,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Math.Max(1, timeoutMilliseconds));

        try
        {
            var results = await provider
                .SearchAsync(query, maximumResults, timeout.Token)
                .ConfigureAwait(false);

            return results;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Launcher search provider {Provider} exceeded its time budget.",
                provider.Name);
            return [];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Launcher search provider {Provider} failed.",
                provider.Name);
            return [];
        }
    }
}
