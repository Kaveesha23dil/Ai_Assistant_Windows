using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.Voice;

/// <summary>
/// Answers the single question every voice handler asks before touching a system service:
/// is this capability consented to right now?
/// <para>
/// Centralizing the decision is what makes "no dangerous command bypasses a permission
/// check" a property of the design rather than a rule each handler has to remember. The
/// capability-to-switch mapping lives here and nowhere else, and the denied sentence is
/// defined next to it so the explanation can never drift from the reason.
/// </para>
/// </summary>
public sealed class PermissionService : IPermissionService
{
    private readonly IOptionsMonitor<PrivacyOptions> _privacy;
    private readonly ILogger<PermissionService> _logger;

    public PermissionService(IOptionsMonitor<PrivacyOptions> privacy, ILogger<PermissionService> logger)
    {
        ArgumentNullException.ThrowIfNull(privacy);
        ArgumentNullException.ThrowIfNull(logger);

        _privacy = privacy;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsGranted(PermissionCapability capability)
    {
        var options = _privacy.CurrentValue;

        return capability switch
        {
            // Voice capture needs both switches: the general voice consent and the specific
            // permission to open the microphone.
            PermissionCapability.Microphone =>
                options.AllowVoiceProcessing && options.AllowMicrophoneAccess,

            PermissionCapability.Clipboard => options.AllowClipboardProcessing,
            PermissionCapability.FileSearch => options.AllowFileIndexing,
            PermissionCapability.ScreenCapture => options.AllowScreenCapture,

            // Analyzing a capture sends screen content off the machine, so it is a separate
            // and stricter consent than simply taking the screenshot.
            PermissionCapability.ScreenAnalysis => options.AllowScreenAnalysis,
            PermissionCapability.SystemControl => options.AllowSystemControl,
            PermissionCapability.ApplicationLaunch => options.AllowApplicationLaunch,
            PermissionCapability.WebSearch => options.AllowWebSearch,
            PermissionCapability.CloudAI => options.AllowCloudAI,
            PermissionCapability.VoiceHistory => options.StoreVoiceHistory,

            _ => false
        };
    }

    /// <inheritdoc />
    public string GetDeniedMessage(PermissionCapability capability) => capability switch
    {
        PermissionCapability.Microphone =>
            "Microphone access is disabled. Turn on voice and microphone access in Settings first.",

        PermissionCapability.Clipboard =>
            "Clipboard access is disabled in Privacy settings.",

        PermissionCapability.FileSearch =>
            "File search is disabled in Privacy settings.",

        PermissionCapability.ScreenCapture =>
            "Screen capture is disabled in Privacy settings.",

        PermissionCapability.ScreenAnalysis =>
            "Screen analysis is disabled in Privacy settings.",

        PermissionCapability.SystemControl =>
            "System control is disabled in Privacy settings.",

        PermissionCapability.ApplicationLaunch =>
            "Opening applications is disabled in Privacy settings.",

        PermissionCapability.WebSearch =>
            "Web search is disabled in Privacy settings.",

        PermissionCapability.CloudAI =>
            "Cloud AI is disabled in Privacy settings.",

        PermissionCapability.VoiceHistory =>
            "Voice history is disabled in Privacy settings.",

        _ => "That capability is not available."
    };

    /// <inheritdoc />
    public Result EnsureGranted(PermissionCapability capability)
    {
        if (IsGranted(capability))
        {
            return Result.Success();
        }

        _logger.LogInformation(
            "Assistant capability {Capability} was refused because it is not consented to.",
            capability);

        return Result.Failure(GetDeniedMessage(capability));
    }
}
