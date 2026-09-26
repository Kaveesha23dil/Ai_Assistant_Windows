using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Application.System.Commands.LaunchApplication;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Voice.Commands.OpenApplication;

/// <summary>
/// Handles "open chrome" and every other way of asking for an application.
/// <para>
/// It delegates to the existing <see cref="LaunchApplicationHandler"/> rather than launching
/// anything itself. The launcher resolves the spoken name against an allow list first, so a
/// spoken sentence never becomes a command line.
/// </para>
/// </summary>
public sealed class OpenApplicationVoiceHandler : VoiceHandlerBase
{
    private static readonly AssistantIntent[] Supported = [AssistantIntent.OpenApplication];

    private readonly LaunchApplicationHandler _launchHandler;

    public OpenApplicationVoiceHandler(
        LaunchApplicationHandler launchHandler,
        IPermissionService permissions,
        ILogger<OpenApplicationVoiceHandler> logger)
        : base(permissions, logger)
    {
        ArgumentNullException.ThrowIfNull(launchHandler);

        _launchHandler = launchHandler;
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

        if (Permissions.IsGranted(PermissionCapability.ApplicationLaunch) is false)
        {
            return Denied(command, PermissionCapability.ApplicationLaunch);
        }

        if (!TryGetRequiredParameter(
                command,
                VoiceCommand.ApplicationParameter,
                out var applicationName,
                out var failure))
        {
            return failure;
        }

        try
        {
            var result = await _launchHandler
                .HandleAsync(new LaunchApplicationCommand(applicationName), cancellationToken)
                .ConfigureAwait(false);

            if (result.IsSuccess)
            {
                return VoiceCommandResult.Success(command, $"Opening {applicationName}.");
            }

            return Unavailable(command, $"I couldn't open {applicationName}.");
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
