using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Application.Common.Errors;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Voice.Services;

/// <summary>
/// Applies the safety policy and dispatches a command to its handler.
/// <para>
/// The order of the checks is the security-relevant part. A restricted action is refused
/// before any handler is consulted, an unconfirmed confirmation-level action becomes a prompt
/// rather than an execution, and only then does the selected handler run. An intent with no
/// registered handler produces the standard "not available yet" answer, which is how
/// unimplemented and unsupported capabilities are refused without a special case.
/// </para>
/// </summary>
public sealed class CommandRouter : ICommandRouter
{
    private const string RestrictedMessage = "That action is not available yet.";
    private const string UnavailableMessage = "That action is not available yet.";

    private readonly IAssistantActionRegistry _registry;
    private readonly ILogger<CommandRouter> _logger;

    public CommandRouter(IAssistantActionRegistry registry, ILogger<CommandRouter> logger)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(logger);

        _registry = registry;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<AssistantIntent> SupportedIntents => _registry.RegisteredIntents;

    /// <inheritdoc />
    public async Task<VoiceCommandResult> RouteAsync(
        VoiceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (command.SafetyLevel == ActionSafetyLevel.Restricted)
        {
            _logger.LogWarning("Refused restricted voice action {Intent}.", command.Intent);
            return VoiceCommandResult.Failure(
                command,
                ErrorCodes.VoiceActionRestricted,
                RestrictedMessage);
        }

        if (!_registry.TryGetExecutor(command.Intent, out var executor) || executor is null)
        {
            _logger.LogInformation(
                "No voice handler is registered for intent {Intent}.",
                command.Intent);
            return VoiceCommandResult.Unavailable(command, UnavailableMessage);
        }

        if (command.RequiresConfirmation && !command.IsConfirmed)
        {
            _logger.LogInformation(
                "Voice action {Intent} is waiting for confirmation.",
                command.Intent);
            return VoiceCommandResult.NeedsConfirmation(
                command,
                $"This would {DescribeAction(command.Intent)}. Say yes to continue, or say cancel.");
        }

        return await executor.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static string DescribeAction(AssistantIntent intent) => intent switch
    {
        AssistantIntent.CloseApplication => "close an application",
        _ => "continue with that action"
    };
}
