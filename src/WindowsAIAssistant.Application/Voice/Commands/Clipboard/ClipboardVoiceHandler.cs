using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Application.Voice.Text;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Abstractions.Clipboard;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Voice.Commands.Clipboard;

/// <summary>
/// Handles "what's on my clipboard" and "summarize my clipboard".
/// <para>
/// Reading the clipboard is a read of data the user never intended to share, so it is behind
/// its own consent switch. Summarizing additionally needs the cloud-AI switch, because that
/// sends the clipboard off the machine; reading never does. The clipboard text itself is never
/// logged and never placed in the result data, only in the sentence the user asked to hear.
/// </para>
/// </summary>
public sealed class ClipboardVoiceHandler : VoiceHandlerBase
{
    private static readonly AssistantIntent[] Supported =
        [AssistantIntent.ReadClipboard, AssistantIntent.SummarizeClipboard];

    private readonly IClipboardService _clipboard;
    private readonly IAIService _ai;

    public ClipboardVoiceHandler(
        IClipboardService clipboard,
        IAIService ai,
        IPermissionService permissions,
        ILogger<ClipboardVoiceHandler> logger)
        : base(permissions, logger)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(ai);

        _clipboard = clipboard;
        _ai = ai;
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

        if (!Permissions.IsGranted(PermissionCapability.Clipboard))
        {
            return Denied(command, PermissionCapability.Clipboard);
        }

        if (command.Intent == AssistantIntent.SummarizeClipboard
            && !Permissions.IsGranted(PermissionCapability.CloudAI))
        {
            return Denied(command, PermissionCapability.CloudAI);
        }

        try
        {
            var text = await _clipboard.GetTextAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text))
            {
                return VoiceCommandResult.Success(command, "Your clipboard is empty.");
            }

            return command.Intent == AssistantIntent.ReadClipboard
                ? VoiceCommandResult.Success(command, TruncateForSpeech(text))
                : await SummarizeAsync(command, text, cancellationToken).ConfigureAwait(false);
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

    private async Task<VoiceCommandResult> SummarizeAsync(
        VoiceCommand command,
        string text,
        CancellationToken cancellationToken)
    {
        var prompt = $"Summarize the following text in two sentences:\n\n{text}";
        var response = await _ai.SendMessageAsync(prompt, cancellationToken).ConfigureAwait(false);

        return !response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content)
            ? Unavailable(command, "I couldn't summarize your clipboard.")
            : VoiceCommandResult.Success(command, TruncateForSpeech(response.Content));
    }

    /// <summary>
    /// Keeps a spoken clipboard reply to a couple of sentences. A full document read back
    /// word for word would be unusable and would keep the microphone open far too long.
    /// </summary>
    private static string TruncateForSpeech(string text) =>
        ResponseTextFormatter.TruncateForSpeech(text, MaximumSpokenCharacters);

    private const int MaximumSpokenCharacters = 400;
}
