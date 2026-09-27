using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.AI;

/// <summary>
/// Chooses the provider named in configuration out of the ones registered.
/// <para>
/// The lookup goes through the registrations rather than a switch, so adding a provider is a
/// new registration and nothing else. A name that resolves to nothing is
/// <see cref="AIProviderType.Unknown"/> and produces no provider: the person is told the
/// selection needs fixing instead of being met with a start-up failure over a spelling.
/// </para>
/// <para>
/// The selection is read from options on each lookup rather than cached for the life of the
/// singleton, so a change to the setting is honoured by the next request. A request already in
/// flight is unaffected: the coordinator resolves once and holds that provider for the whole
/// request, so an answer cannot switch models part-way through being written.
/// </para>
/// </summary>
public sealed class AIProviderFactory : IAIProviderFactory
{
    private readonly IReadOnlyDictionary<AIProviderType, IAIProvider> _providers;
    private readonly IOptionsMonitor<AIOptions> _options;
    private readonly ILogger<AIProviderFactory> _logger;
    private readonly Lock _warnLock = new();

    /// <summary>
    /// The last unrecognised provider name that was reported, so a request arriving every few
    /// seconds against a misspelt name logs once rather than once per request.
    /// </summary>
    private string? _warnedProvider;

    public AIProviderFactory(
        IEnumerable<IAIProvider> providers,
        IOptionsMonitor<AIOptions> options,
        ILogger<AIProviderFactory> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        // A duplicate registration would otherwise make the winner depend on registration
        // order, so the first one is kept and the clash is recorded.
        var table = new Dictionary<AIProviderType, IAIProvider>();
        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);

            if (table.TryAdd(provider.ProviderType, provider))
            {
                continue;
            }

            logger.LogWarning(
                "AI provider type {ProviderType} is registered more than once; keeping the first.",
                provider.ProviderType);
        }

        _providers = table;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<AIProviderType> AvailableProviders => _providers.Keys.ToArray();

    /// <inheritdoc />
    public AIProviderType SelectedProvider => Parse(_options.CurrentValue.Provider);

    /// <inheritdoc />
    public string? SelectedModel
    {
        get
        {
            var configured = _options.CurrentValue.Model;
            return string.IsNullOrWhiteSpace(configured) ? null : configured;
        }
    }

    /// <inheritdoc />
    public IAIProvider? Resolve(AIProviderType providerType)
    {
        if (providerType is AIProviderType.Unknown)
        {
            return null;
        }

        return _providers.GetValueOrDefault(providerType);
    }

    /// <inheritdoc />
    public IAIProvider? ResolveSelected() => Resolve(SelectedProvider);

    /// <summary>
    /// Matches a configured name against the known providers, ignoring case and surrounding
    /// space so a value typed by hand still resolves.
    /// </summary>
    private AIProviderType Parse(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return AIProviderType.Unknown;
        }

        if (Enum.TryParse<AIProviderType>(provider.Trim(), ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        // The name is configuration, so an unrecognised one is recorded rather than treated as
        // a defect. The value is a provider name: never a credential, never a host.
        WarnOnce(provider);
        return AIProviderType.Unknown;
    }

    private void WarnOnce(string provider)
    {
        lock (_warnLock)
        {
            if (string.Equals(_warnedProvider, provider, StringComparison.Ordinal))
            {
                return;
            }

            _warnedProvider = provider;
        }

        _logger.LogWarning(
            "The configured AI provider '{Provider}' is not one this build can serve. Available providers: {Available}.",
            provider,
            string.Join(", ", _providers.Keys));
    }
}
