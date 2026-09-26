using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Application.Voice.Text;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Voice.Commands.AiQuestion;

/// <summary>
/// Handles a free-form question by sending it to the configured AI service.
/// <para>
/// This is the only voice capability that leaves the machine, so it carries the cloud-AI
/// consent switch and is registered last in the intent table. A question is forwarded exactly
/// as the user asked it; the prompt itself is never written to a log, because a spoken
/// question is user content.
/// </para>
/// </summary>
public sealed class AiQuestionVoiceHandler : VoiceHandlerBase
{
    private static readonly AssistantIntent[] Supported = [AssistantIntent.AIQuestion];

    private readonly IAIService _ai;
    private readonly VoiceIntentPolicy _policy;

    public AiQuestionVoiceHandler(
        IAIService ai,
        VoiceIntentPolicy policy,
        IPermissionService permissions,
        ILogger<AiQuestionVoiceHandler> logger)
        : base(permissions, logger)
    {
        ArgumentNullException.ThrowIfNull(ai);
        ArgumentNullException.ThrowIfNull(policy);

        _ai = ai;
        _policy = policy;
    }

    /// <inheritdoc />
    public override IReadOnlyCollection<AssistantIntent> Intents => Supported;

    /// <inheritdoc />
    public override async Task<VoiceCommandResult> ExecuteAsync(
        VoiceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Permissions.IsGranted(PermissionCapability.CloudAI))
        {
            return Denied(command, PermissionCapability.CloudAI);
        }

        if (!TryGetRequiredParameter(
                command,
                VoiceCommand.TextParameter,
                out var question,
                out var failure))
        {
            return failure;
        }

        try
        {
            var response = await _ai.SendMessageAsync(question, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content))
            {
                return Unavailable(command, "I couldn't get an answer to that.");
            }

            // The reply is truncated to the spoken budget; the user interface receives the
            // full text so nothing is lost by asking for a short answer aloud.
            return VoiceCommandResult.Success(
                command,
                ResponseTextFormatter.TruncateForSpeech(response.Content, _policy.MaximumSpokenResponseLength),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["answer"] = response.Content
                });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Failed(command, exception);
        }
    }
}
