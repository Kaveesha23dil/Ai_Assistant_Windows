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

            // Local recognition. Not gated on AllowCloudAI, because the whole point of it is
            // that reading the text off a screen works with nothing leaving the machine.
            PermissionCapability.ScreenOcr => options.AllowScreenOcr,

            // The most consequential combination in the switch statement. Both switches have to
            // be on before a picture of somebody's screen is allowed to leave the machine, and
            // neither being on means the visual path falls back to local recognition rather than
            // quietly uploading.
            PermissionCapability.CloudScreenAnalysis =>
                options.AllowCloudAI && options.AllowCloudScreenAnalysis,

            // About the file rather than the capture. Off means nothing is written to disk, and
            // an in-memory screenshot is unaffected by this either way.
            PermissionCapability.ScreenshotHistory => options.StoreScreenshotHistory,

            PermissionCapability.SystemControl => options.AllowSystemControl,
            PermissionCapability.ApplicationLaunch => options.AllowApplicationLaunch,
            PermissionCapability.WebSearch => options.AllowWebSearch,
            PermissionCapability.CloudAI => options.AllowCloudAI,

            // Its own switch rather than a consequence of AllowCloudAI: a document is content
            // the person did not type, and consenting to chat with a cloud provider is not
            // consenting to post a contract to one.
            PermissionCapability.DocumentCloudProcessing =>
                options.AllowCloudAI && options.AllowDocumentCloudProcessing,

            // Knowledge indexing is local, so it is not gated on AllowCloudAI. Reading, splitting,
            // and storing text on this machine reaches nobody else, and making it depend on a
            // cloud switch would mean a person who never wanted the cloud could not build a
            // local index.
            PermissionCapability.KnowledgeBase => options.AllowKnowledgeBase,

            // Two switches, and neither is enough on its own. Turned on, this means every passage
            // of every indexed document is posted to a third party as a vector — so it requires
            // the cloud switch as well as the one specific to embeddings.
            PermissionCapability.CloudEmbedding =>
                options.AllowCloudAI && options.AllowCloudEmbedding,

            // Required even when the embeddings were made locally, because this is the step that
            // puts retrieved passages in front of a model. Where the vectors went and where the
            // answer is computed are separate questions and both have to be answered yes.
            PermissionCapability.CloudKnowledgeProcessing =>
                options.AllowCloudAI && options.AllowCloudKnowledgeProcessing,

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

        PermissionCapability.ScreenOcr =>
            "Reading text from your screen is disabled in Privacy settings.",

        PermissionCapability.CloudScreenAnalysis =>
            "Sending your screen to a cloud provider is disabled in Privacy settings.",

        PermissionCapability.ScreenshotHistory =>
            "Saving screenshots is disabled in Privacy settings.",

        PermissionCapability.SystemControl =>
            "System control is disabled in Privacy settings.",

        PermissionCapability.ApplicationLaunch =>
            "Opening applications is disabled in Privacy settings.",

        PermissionCapability.WebSearch =>
            "Web search is disabled in Privacy settings.",

        PermissionCapability.CloudAI =>
            "Cloud AI is disabled in Privacy settings.",

        PermissionCapability.DocumentCloudProcessing =>
            "Sending document contents to a cloud provider is disabled in Privacy settings.",

        PermissionCapability.KnowledgeBase =>
            "Building a knowledge base from your documents is disabled in Privacy settings.",

        PermissionCapability.CloudEmbedding =>
            "Sending document text to a cloud service for embeddings is disabled in Privacy settings.",

        PermissionCapability.CloudKnowledgeProcessing =>
            "Answering from your documents in the cloud is disabled in Privacy settings.",

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
