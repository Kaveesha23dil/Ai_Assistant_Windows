using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.Vision;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Vision;
using WindowsAIAssistant.Infrastructure.Configuration.Options;
using IOcrProviderResolver = WindowsAIAssistant.Core.Abstractions.Vision.IOcrProviderResolver;
using IVisionProvider = WindowsAIAssistant.Core.Abstractions.Vision.IVisionProvider;
using IVisionProviderResolver = WindowsAIAssistant.Core.Abstractions.Vision.IVisionProviderResolver;

namespace WindowsAIAssistant.Infrastructure.Vision.Providers;

/// <summary>
/// Chooses who looks at a screenshot, and refuses to choose anyone who may not.
/// <para>
/// The consent check lives here rather than only in the provider, and both have it. That is not
/// redundancy for its own sake: this is the last point at which a provider can be picked, so a
/// check here is a check on the decision itself — it cannot be bypassed by a caller that
/// resolves a provider once and holds onto it — and the check inside the provider catches the
/// case of a provider handed a request it was not resolved for. Neither of them trusts the
/// other.
/// </para>
/// <para>
/// The other invariant is that a provider is never returned for a request that is not entitled
/// to it. A cloud provider is chosen only when the request carries cloud consent; a request
/// without it gets the local provider instead, and that local provider is allowed to answer
/// "I can only read the text" — which is a far better outcome than an error, and a truthful one.
/// </para>
/// </summary>
public sealed class VisionProviderResolver : IVisionProviderResolver
{
    private readonly IReadOnlyList<IVisionProvider> _local;
    private readonly IReadOnlyList<IVisionProvider> _cloud;
    private readonly IScreenCaptureService _capture;
    private readonly IOcrProviderResolver _ocr;
    private readonly IOptionsMonitor<VisionOptions> _options;
    private readonly ILogger<VisionProviderResolver> _logger;

    public VisionProviderResolver(
        IEnumerable<IVisionProvider> providers,
        IScreenCaptureService capture,
        IOcrProviderResolver ocr,
        IOptionsMonitor<VisionOptions> options,
        ILogger<VisionProviderResolver> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(ocr);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _capture = capture;
        _ocr = ocr;
        _options = options;
        _logger = logger;

        var local = new List<IVisionProvider>();
        var cloud = new List<IVisionProvider>();

        foreach (var provider in providers)
        {
            if (provider is null)
            {
                continue;
            }

            (provider.IsCloudHosted ? cloud : local).Add(provider);
        }

        _local = local;
        _cloud = cloud;
    }

    /// <inheritdoc />
    public Result<IVisionProvider> Resolve(VisionPrivacyContext privacy)
    {
        ArgumentNullException.ThrowIfNull(privacy);

        if (!privacy.AnalysisConsent)
        {
            return Result<IVisionProvider>.Failure(
                ErrorCodes.VisionAnalysisPermissionDenied,
                "Looking at the screen is turned off, so nothing was read.");
        }

        if (privacy.AllowsCloudImageSubmission)
        {
            if (_cloud.FirstOrDefault(static provider => provider.IsAvailable) is { } available)
            {
                if (!available.SupportsImageInput)
                {
                    return Result<IVisionProvider>.Failure(
                        ErrorCodes.VisionModelNoImageSupport,
                        $"The configured model ({available.Name}) cannot look at images. Choose one "
                        + "that accepts pictures in Settings — \"read the text\" will still work "
                        + "without one.");
                }

                _logger.LogInformation(
                    "A screenshot will be analysed by {Provider} under cloud consent from {ConsentSource}.",
                    available.Name,
                    privacy.ConsentSource);

                return Result<IVisionProvider>.Success(available);
            }

            _logger.LogInformation(
                "A screenshot was permitted to be analysed but no cloud provider is configured, "
                + "so the local reading was used instead.");
        }
        else
        {
            _logger.LogInformation(
                "A screenshot was analysed locally. Cloud consent for this request is {CloudConsent}.",
                privacy.CloudConsent);
        }

        if (_local.FirstOrDefault() is { } local)
        {
            return Result<IVisionProvider>.Success(local);
        }

        return Result<IVisionProvider>.Failure(
            ErrorCodes.VisionProviderUnavailable,
            "There is no way to look at a screenshot on this computer right now. Reading text may "
            + "still work.");
    }

    /// <inheritdoc />
    /// <remarks>
    /// Facts about this machine, read from the same objects the request path uses, so the status
    /// line cannot drift from what would actually happen. Nothing here starts a capture, calls a
    /// network, or reads a credential's value.
    /// </remarks>
    public Task<VisionCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = _options.CurrentValue;
        var ocrKind = _ocr.Resolve();
        var cloud = _cloud.FirstOrDefault();
        var captureSupported = _capture.IsSupported;

        // Whether something can genuinely look at a picture, which is not the same as whether
        // this resolver can return a provider: the local reading is a fallback that reports text,
        // and calling that "a model that can see" would overstate the machine.
        var modelSees = _cloud.Any(static provider => provider.IsAvailable && provider.SupportsImageInput);

        return Task.FromResult(new VisionCapabilities(
            captureSupported: captureSupported,
            ocrProvider: ocrKind,
            ocrAvailable: ocrKind != OcrProviderKind.None,
            modelSupportsImageInput: modelSees,
            cloudConfigured: cloud is { IsAvailable: true },
            cloudVisionAvailable: modelSees,
            imageSizeLimitBytes: options.MaxImageBytes,
            maxDimension: options.MaxImageDimension,
            source: $"capture:{(captureSupported ? "yes" : "no")}, text:{ocrKind}"));
    }
}
