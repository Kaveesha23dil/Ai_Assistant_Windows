using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.Embeddings;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.Embeddings;

/// <summary>
/// Picks the configured embedding provider out of the registered ones.
/// <para>
/// The same shape as <c>AIProviderFactory</c>, and for the same reason: the collection of
/// providers is the extension point, so a new one is a single registration here and nothing
/// above Infrastructure changes to use it.
/// </para>
/// <para>
/// An unrecognized name resolves to the development provider rather than failing. A knowledge
/// base is something a person uses before they have finished configuring the assistant, and
/// refusing every question about their documents because a spelling is wrong in a configuration
/// file would be a poor answer to a small mistake. The search that results is worse and is
/// labelled as such, and the configuration is reported at start-up so the mistake is visible.
/// </para>
/// </summary>
public sealed class EmbeddingProviderFactory : IEmbeddingProviderFactory
{
    private readonly IReadOnlyList<IEmbeddingProvider> _providers;
    private readonly IOptionsMonitor<EmbeddingOptions> _options;
    private readonly ILogger<EmbeddingProviderFactory> _logger;

    public EmbeddingProviderFactory(
        IEnumerable<IEmbeddingProvider> providers,
        IOptionsMonitor<EmbeddingOptions> options,
        ILogger<EmbeddingProviderFactory> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _providers = [.. providers];
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public IEmbeddingProvider Resolve(string? name)
    {
        var fallback = _providers.FirstOrDefault(provider => provider.ProviderKind == EmbeddingProviderKind.Mock)
            ?? throw new InvalidOperationException(
                "No development embedding provider is registered, so no provider can be resolved.");

        if (string.IsNullOrWhiteSpace(name))
        {
            return fallback;
        }

        var requested = name.Trim();

        foreach (var provider in _providers)
        {
            if (string.Equals(provider.Name, requested, StringComparison.OrdinalIgnoreCase))
            {
                return provider;
            }
        }

        _logger.LogWarning(
            "The configured embedding provider {Provider} is not one this build provides, so the development provider is used instead.",
            requested);

        return fallback;
    }

    /// <summary>
    /// Reports the configured provider once, for a start-up line that shows which one is in
    /// effect without anybody having to open a file to find out. Holds no text and no credential.
    /// </summary>
    internal string DescribeConfigured() =>
        $"{_options.CurrentValue.Provider} / {_options.CurrentValue.Model ?? "provider default"}";
}
