using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Application.Common.Errors;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Voice.Commands.AssistantControl;

/// <summary>
/// Handles the commands that steer the assistant itself: start and stop listening, cancel,
/// stop speaking, repeat the last reply, and read text aloud.
/// <para>
/// This is the one handler that talks back to the assistant, and it does so through
/// <see cref="IVoiceAssistantControl"/> rather than through the assistant service. That narrow
/// surface is what keeps the object graph free of a cycle.
/// </para>
/// </summary>
public sealed class AssistantControlVoiceHandler : VoiceHandlerBase
{
    private static readonly AssistantIntent[] Supported =
    [
        AssistantIntent.StartListening,
        AssistantIntent.StopListening,
        AssistantIntent.Cancel,
        AssistantIntent.StopSpeaking,
        AssistantIntent.RepeatResponse,
        AssistantIntent.SpeakText
    ];

    private readonly IVoiceAssistantControl _control;

    public AssistantControlVoiceHandler(
        IVoiceAssistantControl control,
        IPermissionService permissions,
        ILogger<AssistantControlVoiceHandler> logger)
        : base(permissions, logger)
    {
        ArgumentNullException.ThrowIfNull(control);

        _control = control;
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

        try
        {
            return command.Intent switch
            {
                AssistantIntent.StartListening => await ReportAsync(
                    command,
                    _control.StartListeningAsync(cancellationToken),
                    "I'm listening.").ConfigureAwait(false),
                AssistantIntent.StopListening => await ReportAsync(
                    command,
                    _control.StopListeningAsync(cancellationToken),
                    "Stopped listening.").ConfigureAwait(false),
                AssistantIntent.Cancel => await ReportAsync(
                    command,
                    _control.CancelAsync(cancellationToken),
                    "Cancelled.").ConfigureAwait(false),
                AssistantIntent.StopSpeaking => await ReportAsync(
                    command,
                    _control.StopSpeakingAsync(cancellationToken),
                    "Stopped talking.").ConfigureAwait(false),
                AssistantIntent.RepeatResponse => await RepeatAsync(command, cancellationToken)
                    .ConfigureAwait(false),
                AssistantIntent.SpeakText => await SpeakTextAsync(command, cancellationToken)
                    .ConfigureAwait(false),
                _ => VoiceCommandResult.Unavailable(command, "That action is not available yet.")
            };
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

    private async Task<VoiceCommandResult> RepeatAsync(VoiceCommand command, CancellationToken cancellationToken)
    {
        var last = _control.LastResponse;
        if (string.IsNullOrWhiteSpace(last))
        {
            return Invalid(command, "There's nothing to repeat yet.");
        }

        // The reply is replayed through synthesis, so the spoken output is identical to the
        // first time rather than a re-run of the action.
        var spoken = await _control.SpeakAsync(last, cancellationToken).ConfigureAwait(false);
        return spoken.IsFailure
            ? Unavailable(command, "I couldn't repeat that.")
            : VoiceCommandResult.Success(command, "Again.");
    }

    private async Task<VoiceCommandResult> SpeakTextAsync(VoiceCommand command, CancellationToken cancellationToken)
    {
        if (!TryGetRequiredParameter(
                command,
                VoiceCommand.TextParameter,
                out var text,
                out var failure))
        {
            return failure;
        }

        var spoken = await _control.SpeakAsync(text, cancellationToken).ConfigureAwait(false);
        return spoken.IsFailure
            ? Unavailable(command, "I couldn't read that aloud.")
            : VoiceCommandResult.Success(command, "Reading that back.");
    }

    /// <summary>
    /// Turns a control result into a spoken answer, keeping the control surface's own message
    /// so a refused action, such as a missing microphone consent, is explained rather than
    /// replaced with a generic error.
    /// </summary>
    private static async Task<VoiceCommandResult> ReportAsync(
        VoiceCommand command,
        Task<Result> operation,
        string successMessage)
    {
        var result = await operation.ConfigureAwait(false);

        return result.IsSuccess
            ? VoiceCommandResult.Success(command, successMessage)
            : VoiceCommandResult.Failure(
                command,
                ErrorCodes.VoiceActionFailed,
                result.ErrorMessage ?? "I couldn't do that.");
    }
}
