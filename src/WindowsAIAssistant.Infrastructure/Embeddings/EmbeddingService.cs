using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.Embeddings;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models.Embeddings;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.Embeddings;

/// <summary>
/// The single place a vector is asked for, and the single place consent is checked for it.
/// <para>
/// Everything that policy needs lives here: which provider is configured, whether the text is
/// about to leave the machine, whether the person has agreed to that, how long the text may be,
/// how many texts go in one request, and what a failure is called. Callers above this class name
/// <see cref="Core.Abstractions.Embeddings.IEmbeddingService"/> and nothing else, which is what
/// turns "document text is never sent to a provider without permission" into a property of the
/// design — there is no second path to the providers, so there is no second place to forget.
/// </para>
/// <para>
/// The consent check is here rather than in the provider on purpose. A provider asked directly
/// would have to check it, and the next provider would have to remember; here the check is
/// unavoidable because there is nowhere else to be.
/// </para>
/// </summary>
public sealed class EmbeddingService : IEmbeddingService
{
    private readonly IPermissionService _permissions;
    private readonly IEmbeddingProviderFactory _providers;
    private readonly IOptionsMonitor<EmbeddingOptions> _options;
    private readonly ILogger<EmbeddingService> _logger;

    public EmbeddingService(
        IEmbeddingProviderFactory providers,
        IPermissionService permissions,
        IOptionsMonitor<EmbeddingOptions> options,
        ILogger<EmbeddingService> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _providers = providers;
        _permissions = permissions;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public EmbeddingProviderKind ActiveProvider => _providers.Resolve(Options.Provider).ProviderKind;

    /// <inheritdoc />
    public string? ActiveModel
    {
        get
        {
            var configured = Options.Model;
            return string.IsNullOrWhiteSpace(configured)
                ? null
                : configured.Trim();
        }
    }

    /// <inheritdoc />
    public string? ResolvedModel
    {
        get
        {
            var provider = _providers.Resolve(Options.Provider);
            return ActiveModel ?? provider.DefaultModel;
        }
    }

    /// <inheritdoc />
    public EmbeddingSpace? ActiveSpace
    {
        get
        {
            var provider = _providers.Resolve(Options.Provider);

            // Only reported when the width is known without making a request. A cloud provider's
            // width belongs to its model, and guessing it would put a wrong dimension into the
            // index — so it stays unknown until a real vector arrives, and the indexer reads the
            // space off the first vector instead. The model, by contrast, is always known, which
            // is why the compatibility check does not need this to be non-null.
            return provider.KnownDimensions is { } dimensions && ResolvedModel is { } model
                ? new EmbeddingSpace(provider.Name, model, dimensions)
                : null;
        }
    }

    /// <inheritdoc />
    public bool IsCompatibleWith(EmbeddingSpace? space)
    {
        if (space is null)
        {
            return false;
        }

        var provider = _providers.Resolve(Options.Provider);

        return string.Equals(space.Provider, provider.Name, StringComparison.OrdinalIgnoreCase)
            && ResolvedModel is { } model
            && string.Equals(space.Model, model, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public bool IsLocalProvider => EmbeddingProviderKinds.IsLocal(ActiveProvider);

    /// <inheritdoc />
    public bool IsAvailable => _providers.Resolve(Options.Provider).IsAvailable;

    /// <inheritdoc />
    public async Task<EmbeddingVector> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var provider = ResolvePermitted();
        var truncated = Truncate(text, Options.MaximumInputCharacters);

        return await provider.GenerateAsync(truncated, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmbeddingVector>> GenerateBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        if (texts.Count == 0)
        {
            return [];
        }

        var provider = ResolvePermitted();

        var maximum = Options.MaximumInputCharacters;
        var results = new List<EmbeddingVector>(texts.Count);
        var batchSize = Math.Max(1, Options.BatchSize);

        // One loop rather than a call per text. A document of 400 passages sent 400 at a time is
        // 400 requests against a service that rate-limits and charges per request, and the cost
        // of that mistake is only visible after somebody indexes a real file.
        for (var offset = 0; offset < texts.Count; offset += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var count = Math.Min(batchSize, texts.Count - offset);
            var batch = new List<string>(count);

            for (var index = 0; index < count; index++)
            {
                batch.Add(Truncate(texts[offset + index], maximum));
            }

            var generated = await provider.GenerateBatchAsync(batch, cancellationToken).ConfigureAwait(false);

            if (generated.Count != count)
            {
                // A provider that returns a different number of vectors than it was given texts
                // has misaligned the results, and pairing them by position would attribute the
                // wrong meaning to the wrong passage. That is worse than failing.
                throw new KnowledgeException(
                    "The embedding provider returned a different number of results than passages. Try again.",
                    ErrorCodes.KnowledgeEmbeddingInvalid);
            }

            results.AddRange(generated);
        }

        return results;
    }

    private EmbeddingOptions Options => _options.CurrentValue;

    /// <summary>
    /// Resolves the configured provider and checks that the text may go to it.
    /// </summary>
    /// <remarks>
    /// The permission is asked about the provider's own locality rather than about the
    /// configured name, so a provider that turns out to be remote cannot be reached by naming it
    /// differently in configuration. A refusal raises a <see cref="KnowledgeException"/> carrying
    /// the stable code rather than returning a failure, because a caller that ignored a failure
    /// here would index documents with no vectors and report them as searchable.
    /// </remarks>
    private IEmbeddingProvider ResolvePermitted()
    {
        var provider = _providers.Resolve(Options.Provider);

        if (provider.IsCloudHosted && !_permissions.IsGranted(PermissionCapability.CloudEmbedding))
        {
            _logger.LogInformation(
                "Embeddings were not generated because sending document text to {Provider} is not consented to.",
                provider.Name);

            throw new KnowledgeException(
                _permissions.GetDeniedMessage(PermissionCapability.CloudEmbedding),
                ErrorCodes.KnowledgeEmbeddingPermissionDenied);
        }

        if (!provider.IsAvailable)
        {
            throw new KnowledgeException(
                $"The {provider.Name} embedding provider is not available. Configure it, or choose another provider.",
                ErrorCodes.KnowledgeEmbeddingUnavailable);
        }

        return provider;
    }

    /// <summary>
    /// Cuts a passage to what the provider will accept.
    /// </summary>
    /// <remarks>
    /// A cut rather than a failure. A provider that refuses an over-long input would otherwise
    /// fail the whole document, and a document with one very long table in it is an ordinary
    /// document — the passages either side of the table are exactly the ones somebody wants found.
    /// The cut is at a word boundary so a passage never ends mid-word in the index.
    /// </remarks>
    private static string Truncate(string text, int maximumCharacters)
    {
        if (maximumCharacters <= 0 || text.Length <= maximumCharacters)
        {
            return text;
        }

        var cut = text.LastIndexOf(' ', maximumCharacters);
        if (cut <= maximumCharacters / 2)
        {
            cut = maximumCharacters;
        }

        return text[..cut];
    }

    /// <summary>
    /// The configuration as it stands, for the settings page and for a log line written once at
    /// start-up. Holds no credential and no text.
    /// </summary>
    internal string DescribeActiveConfiguration() =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{Options.Provider} / {ActiveModel ?? "provider default"} / batch {Options.BatchSize}");

    /// <summary>
    /// Validates the numbers the options carry, once, so a configuration of zero or less fails at
    /// start-up rather than producing an infinite loop or a division by nothing later.
    /// </summary>
    internal IEnumerable<string> Validate() => Validate(Options);

    /// <summary>Validates the embedding options, shared by the validator and by tests.</summary>
    public static IEnumerable<string> Validate(EmbeddingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!EmbeddingProviderKinds.SupportedNames.Contains(options.Provider, StringComparer.OrdinalIgnoreCase))
        {
            yield return string.Format(
                CultureInfo.InvariantCulture,
                "Embeddings:Provider must be one of {0}.",
                string.Join(", ", EmbeddingProviderKinds.SupportedNames));
        }

        if (options.BatchSize <= 0)
        {
            yield return "Embeddings:BatchSize must be greater than zero.";
        }

        if (options.MaximumInputCharacters <= 0)
        {
            yield return "Embeddings:MaximumInputCharacters must be greater than zero.";
        }
    }
}
