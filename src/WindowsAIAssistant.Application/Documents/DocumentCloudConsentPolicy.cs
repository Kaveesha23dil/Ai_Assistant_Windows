using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Abstractions.Documents;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;

namespace WindowsAIAssistant.Application.Documents;

/// <summary>
/// Decides whether a document's text may be sent to the AI provider in use.
/// <para>
/// A document is different from a typed question in a way that matters here: a question is
/// something a person has chosen to send, while a document may be hundreds of pages that
/// arrived by download. So this asks for a separate permission on top of the existing cloud-AI
/// one, and answers "no" until somebody turns it on deliberately.
/// </para>
/// <para>
/// A provider running on this machine is exempt, because the text does not leave. Everything
/// else is treated as remote, including providers this build does not recognize, so an
/// unfamiliar provider is asked about rather than trusted with a document.
/// </para>
/// </summary>
public sealed class DocumentCloudConsentPolicy
{
    private readonly IPermissionService _permissions;
    private readonly IAIService _ai;
    private readonly ILogger<DocumentCloudConsentPolicy> _logger;

    public DocumentCloudConsentPolicy(
        IPermissionService permissions,
        IAIService ai,
        ILogger<DocumentCloudConsentPolicy> logger)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(ai);
        ArgumentNullException.ThrowIfNull(logger);

        _permissions = permissions;
        _ai = ai;
        _logger = logger;
    }

    /// <summary>
    /// Throws when the document's text may not be sent to the provider in use.
    /// </summary>
    public void EnsureAllowed()
    {
        if (AIProviderTypes.IsLocal(_ai.ActiveProvider))
        {
            return;
        }

        if (!_permissions.IsGranted(PermissionCapability.CloudAI))
        {
            _logger.LogInformation(
                "Document analysis refused: the provider in use is remote and cloud AI is not permitted.");

            throw new DocumentException(
                "This provider sends what it receives off your computer, and cloud AI is not turned on.",
                ErrorCodes.DocumentAiPermissionDenied);
        }

        if (!_permissions.IsGranted(PermissionCapability.DocumentCloudProcessing))
        {
            _logger.LogInformation(
                "Document analysis refused: the provider in use is remote and document processing is not permitted.");

            throw new DocumentException(
                "Sending document contents to a cloud provider is not turned on. Turn on \"send documents to cloud providers\" in Settings, or choose a provider that runs on this computer.",
                ErrorCodes.DocumentAiPermissionDenied);
        }
    }
}
