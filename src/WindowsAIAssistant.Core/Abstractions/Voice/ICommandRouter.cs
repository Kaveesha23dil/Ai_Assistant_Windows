using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Core.Abstractions.Voice;

/// <summary>
/// Dispatches a recognized command to the executor registered for its intent, applying the
/// safety rules first so a handler is never reached by an action that should be refused.
/// </summary>
public interface ICommandRouter
{
    /// <summary>Gets the intents that can currently be routed.</summary>
    IReadOnlyCollection<AssistantIntent> SupportedIntents { get; }

    /// <summary>Applies the safety policy and then executes the command.</summary>
    Task<VoiceCommandResult> RouteAsync(
        VoiceCommand command,
        CancellationToken cancellationToken = default);
}
