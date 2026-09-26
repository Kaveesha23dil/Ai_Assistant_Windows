using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Application.Common.Errors;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Voice.Commands;

/// <summary>
/// Shared plumbing for the voice command handlers: consent checks, uniform failure shaping,
/// and the guarantee that a technical exception becomes a user-safe sentence.
/// <para>
/// Every handler runs its permission check before it touches a system service. Concentrating
/// that here is what makes "no command reaches a system service without consent" a property
/// of the base class rather than a rule each handler has to remember.
/// </para>
/// </summary>
public abstract class VoiceHandlerBase : IAssistantActionExecutor
{
    private const string ActionFailedMessage = "I couldn't complete that action.";

    protected VoiceHandlerBase(IPermissionService permissions, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(logger);

        Permissions = permissions;
        Logger = logger;
    }

    /// <inheritdoc />
    public abstract IReadOnlyCollection<AssistantIntent> Intents { get; }

    /// <inheritdoc />
    public abstract Task<VoiceCommandResult> ExecuteAsync(
        VoiceCommand command,
        CancellationToken cancellationToken = default);

    protected IPermissionService Permissions { get; }

    protected ILogger Logger { get; }

    /// <summary>Returns the denied result for a capability the user has not consented to.</summary>
    protected VoiceCommandResult Denied(VoiceCommand command, PermissionCapability capability)
    {
        Logger.LogInformation(
            "Voice action {Intent} was refused: {Capability} is not granted.",
            command.Intent,
            capability);

        return VoiceCommandResult.Failure(
            command,
            ErrorCodes.VoicePermissionDenied,
            Permissions.GetDeniedMessage(capability),
            isPermissionDenied: true);
    }

    /// <summary>Returns a validation failure with a user-safe message.</summary>
    protected static VoiceCommandResult Invalid(VoiceCommand command, string message) =>
        VoiceCommandResult.Failure(command, ErrorCodes.ValidationError, message);

    /// <summary>Returns the standard failure for a service that reported an error.</summary>
    protected static VoiceCommandResult Unavailable(VoiceCommand command, string message) =>
        VoiceCommandResult.Failure(command, ErrorCodes.VoiceActionFailed, message);

    /// <summary>
    /// Converts an unexpected exception into a user-safe failure. The exception object is
    /// logged for diagnostics, but nothing from it is copied into the spoken message.
    /// </summary>
    protected VoiceCommandResult Failed(VoiceCommand command, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        Logger.LogError(exception, "Voice action {Intent} failed.", command.Intent);
        return VoiceCommandResult.Failure(command, ErrorCodes.VoiceActionFailed, ActionFailedMessage);
    }

    /// <summary>
    /// Reads a required parameter and produces the failure to speak when it is missing, so a
    /// half-recognized command never reaches a service.
    /// </summary>
    protected static bool TryGetRequiredParameter(
        VoiceCommand command,
        string name,
        out string value,
        out VoiceCommandResult failure)
    {
        var parameter = command.GetParameter(name);
        if (parameter is null)
        {
            value = string.Empty;
            failure = Invalid(command, $"I didn't catch which {name} you meant.");
            return false;
        }

        value = parameter;
        failure = null!;
        return true;
    }
}
