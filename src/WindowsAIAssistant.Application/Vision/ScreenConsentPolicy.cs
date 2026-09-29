using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Abstractions.Vision;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Vision;

namespace WindowsAIAssistant.Application.Vision;

/// <summary>
/// Decides whether a screenshot may be sent to the provider that would look at it.
/// <para>
/// Three separate questions, asked in order, because they are three separate decisions that
/// people routinely bundle together and should not:
/// </para>
/// <list type="number">
/// <item>May the assistant look at the screen at all?</item>
/// <item>Is the provider in use running on this machine?</item>
/// <item>May a picture of the screen be sent off it?</item>
/// </list>
/// <para>
/// The second question is what keeps a local model usable: an on-device model needs no cloud
/// permission, because nothing leaves. Anything unrecognized is treated as remote, so a
/// provider this build has never heard of is asked rather than trusted with a screenshot.
/// </para>
/// <para>
/// This is the only place a cloud screen analysis is permitted, and it returns the permission
/// rather than throwing on the happy path, so the consent that was in force travels with the
/// request and a provider can see what it was given.
/// </para>
/// </summary>
public sealed class ScreenConsentPolicy
{
    private readonly IPermissionService _permissions;
    private readonly IVisionProviderResolver _providers;
    private readonly ILogger<ScreenConsentPolicy> _logger;

    public ScreenConsentPolicy(
        IPermissionService permissions,
        IVisionProviderResolver providers,
        ILogger<ScreenConsentPolicy> logger)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(logger);

        _permissions = permissions;
        _providers = providers;
        _logger = logger;
    }

    /// <summary>
    /// Returns the permission in force for a cloud-hosted provider, or a failed result naming
    /// the switch that is off.
    /// </summary>
    public Result<VisionPrivacyContext> ResolveForCloudProvider()
    {
        if (!_permissions.IsGranted(PermissionCapability.ScreenCapture))
        {
            _logger.LogInformation("Screen vision refused: screen capture is not permitted.");
            return Result<VisionPrivacyContext>.Failure(
                ErrorCodes.VisionAnalysisPermissionDenied,
                "Taking a screenshot is not turned on. Turn on \"allow screen capture\" in Settings.");
        }

        if (!_permissions.IsGranted(PermissionCapability.ScreenAnalysis))
        {
            _logger.LogInformation("Screen vision refused: screen analysis is not permitted.");
            return Result<VisionPrivacyContext>.Failure(
                ErrorCodes.VisionAnalysisPermissionDenied,
                "Looking at your screen is not turned on. Turn on \"allow screen analysis\" in Settings.");
        }

        if (!_permissions.IsGranted(PermissionCapability.CloudScreenAnalysis))
        {
            _logger.LogInformation(
                "Screen vision refused: sending a screenshot to a cloud provider is not permitted.");

            return Result<VisionPrivacyContext>.Failure(
                ErrorCodes.VisionCloudPermissionDenied,
                "Sending a picture of your screen to a cloud provider is not turned on. " +
                "Turn on \"send screenshots to cloud providers\" in Settings if you want that, " +
                "or choose a provider that runs on this computer.");
        }

        return Result<VisionPrivacyContext>.Success(
            new VisionPrivacyContext(VisionConsentSource.CloudConsent, analysisConsent: true, cloudConsent: true));
    }

    /// <summary>
    /// Returns the permission in force for a provider that runs on this machine.
    /// </summary>
    /// <remarks>
    /// Capture and analysis are still required. Neither is implied by the provider being local:
    /// the picture does not leave, but it is still a picture of somebody's screen, and "run
    /// everything locally" is a statement about where data goes rather than about whether the
    /// screen may be read at all.
    /// </remarks>
    public Result<VisionPrivacyContext> ResolveForLocalProvider()
    {
        if (!_permissions.IsGranted(PermissionCapability.ScreenCapture))
        {
            _logger.LogInformation("Screen vision refused: screen capture is not permitted.");
            return Result<VisionPrivacyContext>.Failure(
                ErrorCodes.VisionAnalysisPermissionDenied,
                "Taking a screenshot is not turned on. Turn on \"allow screen capture\" in Settings.");
        }

        if (!_permissions.IsGranted(PermissionCapability.ScreenAnalysis))
        {
            _logger.LogInformation("Screen vision refused: screen analysis is not permitted.");
            return Result<VisionPrivacyContext>.Failure(
                ErrorCodes.VisionAnalysisPermissionDenied,
                "Looking at your screen is not turned on. Turn on \"allow screen analysis\" in Settings.");
        }

        return Result<VisionPrivacyContext>.Success(VisionPrivacyContext.LocalOnly);
    }

    /// <summary>
    /// Returns the permission in force for the provider that would serve a request, deciding
    /// which of the two questions to ask from whether that provider is remote.
    /// </summary>
    public async Task<Result<VisionPrivacyContext>> ResolveAsync(
        CancellationToken cancellationToken = default)
    {
        var capabilities = await _providers.GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);

        return capabilities.CloudVisionAvailable
            ? ResolveForCloudProvider()
            : ResolveForLocalProvider();
    }
}
