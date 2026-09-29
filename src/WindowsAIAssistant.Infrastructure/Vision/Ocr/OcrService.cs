using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models.Vision;
using IOcrProvider = WindowsAIAssistant.Core.Abstractions.Vision.IOcrProvider;
using IOcrProviderResolver = WindowsAIAssistant.Core.Abstractions.Vision.IOcrProviderResolver;
using IOcrService = WindowsAIAssistant.Core.Abstractions.Vision.IOcrService;

namespace WindowsAIAssistant.Infrastructure.Vision.Ocr;

/// <summary>
/// Reads text out of an image on this machine, using whichever engine was resolved.
/// <para>
/// Local, and local in the strong sense: every provider reachable from here is a Windows
/// in-process component, and this class has no member that takes a credential, opens a socket,
/// or consults a consent switch about the network. "Read what's on my screen" keeps working
/// with the machine offline, which is the whole reason it exists separately from the path that
/// asks a model to look at a picture.
/// </para>
/// <para>
/// It returns an empty result rather than raising when there is no engine or no text. An empty
/// answer is a valid answer to "what does it say", and <see cref="OcrResult.Provider"/> is what
/// lets a caller tell the two apart without inspecting a code.
/// </para>
/// </summary>
public sealed class OcrService : IOcrService
{
    private readonly IOcrProviderResolver _resolver;
    private readonly IReadOnlyDictionary<OcrProviderKind, IOcrProvider> _providers;
    private readonly ILogger<OcrService> _logger;

    public OcrService(
        IOcrProviderResolver resolver,
        IEnumerable<IOcrProvider> providers,
        ILogger<OcrService> logger)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(logger);

        _resolver = resolver;
        _logger = logger;

        var table = new Dictionary<OcrProviderKind, IOcrProvider>();

        foreach (var provider in providers)
        {
            if (provider is not null)
            {
                table[provider.Kind] = provider;
            }
        }

        _providers = table;
    }

    /// <inheritdoc />
    public bool IsAvailable => _resolver.Resolve() != OcrProviderKind.None;

    /// <inheritdoc />
    public OcrProviderKind ProviderKind => _resolver.Resolve();

    /// <inheritdoc />
    /// <exception cref="ScreenVisionException">
    /// Thrown with a code from <see cref="ErrorCodes"/> when the image cannot be read at all.
    /// Never thrown for a missing engine or for an image with no text in it.
    /// </exception>
    public async Task<OcrResult> ReadTextAsync(
        ReadOnlyMemory<byte> imageBytes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var kind = _resolver.Resolve();

        if (kind == OcrProviderKind.None || !_providers.TryGetValue(kind, out var provider))
        {
            _logger.LogInformation("A screenshot was offered for text reading, but this machine "
                + "has no recogniser that may be used.");
            return OcrResult.Empty;
        }

        var result = await provider.ReadAsync(imageBytes, cancellationToken).ConfigureAwait(false);

        if (!result.HasText)
        {
            // Not a failure and not worth a warning. Plenty of perfectly good screenshots have
            // no words in them, and treating that as a problem would train somebody to ignore
            // the status area.
            _logger.LogInformation("No text was recognised in a screenshot.");
        }

        return result;
    }
}
