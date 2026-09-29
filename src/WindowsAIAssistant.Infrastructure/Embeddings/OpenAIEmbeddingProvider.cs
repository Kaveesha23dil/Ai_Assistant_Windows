using System.ClientModel;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Embeddings;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Abstractions.Embeddings;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models.Embeddings;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.Embeddings;

/// <summary>
/// Produces vectors from OpenAI's embedding models, and is the only embedding type that knows
/// they exist.
/// <para>
/// The credential comes from the same <see cref="IAIApiKeyProvider"/> the chat path uses, out of
/// the same environment variable, and is never stored, logged, or included in a message. The
/// client is built per call for the same reason <c>OpenAIProvider</c> builds its own: the key is
/// read at the moment it is needed, a change to the environment takes effect on the next call,
/// and nothing holds a credential in a field for the life of the process.
/// </para>
/// <para>
/// The model is left to the SDK when configuration does not name one, which is why the space
/// this provider reports is only known once a vector has actually arrived. The index stores the
/// model and the width beside every vector regardless, so an index written by this provider is
/// self-describing: nothing has to be looked up later to know what produced a stored vector, and
/// a model that is renamed by its owner cannot make an old index silently incomparable.
/// </para>
/// <para>
/// This provider is never called without the cloud-embedding permission having been checked.
/// That check is not here — it belongs to the service that owns consent, so that it cannot be
/// forgotten by adding a second caller — and it is why no caller in this application talks to
/// this type directly.
/// </para>
/// </summary>
public sealed class OpenAIEmbeddingProvider : IEmbeddingProvider
{
    private const string ProviderName = "OpenAI";

    /// <summary>
    /// The model used when configuration does not name one. Pinned here rather than left to the
    /// SDK so that a provider-side default change cannot silently change the meaning of every
    /// vector in an existing index without anything on this side noticing.
    /// </summary>
    public const string DefaultModelName = "text-embedding-3-small";

    private readonly IAIApiKeyProvider _apiKey;
    private readonly IOptionsMonitor<EmbeddingOptions> _options;
    private readonly ILogger<OpenAIEmbeddingProvider> _logger;

    public OpenAIEmbeddingProvider(
        IAIApiKeyProvider apiKey,
        IOptionsMonitor<EmbeddingOptions> options,
        ILogger<OpenAIEmbeddingProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(apiKey);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _apiKey = apiKey;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public EmbeddingProviderKind ProviderKind => EmbeddingProviderKind.OpenAI;

    /// <inheritdoc />
    public string? DefaultModel => DefaultModelName;

    /// <inheritdoc />
    public bool IsCloudHosted => true;

    /// <inheritdoc />
    public bool IsAvailable => _apiKey.HasApiKey;

    /// <summary>
    /// Not known in advance, and deliberately not hard-coded: the width belongs to the model
    /// rather than to the provider, and a wrong constant here would be a wrong number in every
    /// vector's stored space.
    /// </summary>
    public int? KnownDimensions => null;

    /// <inheritdoc />
    public async Task<EmbeddingVector> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var generated = await GenerateBatchAsync([text], cancellationToken).ConfigureAwait(false);
        return generated[0];
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

        var model = ResolveModel();
        var normalize = Options.NormalizeVectors;
        var client = CreateClient(model);

        try
        {
            // The model travels on the client rather than in the options, so the client is built
            // with it rather than after. The SDK takes the inputs as plain strings here, which is
            // the only reason this call differs from the chat path's shape.
            var operation = await client
                .GenerateEmbeddingsAsync(texts, options: null, cancellationToken)
                .ConfigureAwait(false);

            var results = new List<EmbeddingVector>(texts.Count);

            foreach (var embedding in operation.Value)
            {
                // The SDK's own values are copied into the vector rather than referenced, so a
                // client-side buffer being reused for the next request cannot alter a vector that
                // has already been written to the index.
                results.Add(new EmbeddingVector(
                    embedding.ToFloats().Span,
                    model,
                    ProviderName,
                    normalize));
            }

            return results;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ClientResultException exception)
        {
            _logger.LogWarning(
                "The embedding provider rejected the request with status {Status}.",
                exception.Status);

            throw new KnowledgeException(
                exception.Status switch
                {
                    401 or 403 => "The embedding provider rejected the API key.",
                    429 => "The embedding provider is busy. Wait a moment and try again.",
                    _ => "The embedding provider could not embed that text.",
                },
                exception.Status switch
                {
                    401 or 403 => ErrorCodes.AiAuthenticationFailed,
                    429 => ErrorCodes.AiRateLimited,
                    _ => ErrorCodes.KnowledgeEmbeddingFailed,
                },
                exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            // Network-shaped failures only. Anything else is a bug rather than a condition to
            // report to a person, and swallowing one of those would hide it.
            _logger.LogError(exception, "The embedding provider could not be reached.");
            throw new KnowledgeException(
                "The embedding provider could not be reached. Check your connection and try again.",
                ErrorCodes.AiNetworkFailure,
                exception);
        }
    }

    private EmbeddingOptions Options => _options.CurrentValue;

    private string ResolveModel() =>
        string.IsNullOrWhiteSpace(Options.Model) ? DefaultModelName : Options.Model.Trim();

    private EmbeddingClient CreateClient(string model)
    {
        var apiKey = _apiKey.GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning(
                "No API key is available in {Variable}, so the text was not sent to the embedding provider.",
                Options.ApiKeyEnvironmentVariable);

            throw new KnowledgeException(
                $"No API key is configured. Set the {Options.ApiKeyEnvironmentVariable} environment variable and try again.",
                ErrorCodes.AiCredentialMissing);
        }

        return new EmbeddingClient(model, apiKey);
    }

    /// <summary>
    /// A short description of the current configuration, for the settings page. Contains no
    /// credential and no text.
    /// </summary>
    internal string DescribeConfiguration() =>
        new StringBuilder()
            .Append(ProviderName)
            .Append(" / ")
            .Append(ResolveModel())
            .Append(" / ")
            .Append(Options.BatchSize)
            .Append(" per request")
            .ToString();
}
