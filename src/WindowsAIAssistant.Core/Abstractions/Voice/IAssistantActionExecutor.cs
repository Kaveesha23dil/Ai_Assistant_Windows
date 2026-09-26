using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Core.Abstractions.Voice;

/// <summary>
/// Executes a small family of closely related intents. The router selects an implementation
/// by looking its intent up in the registry, which keeps the routing table a dictionary
/// lookup rather than a switch statement that grows with every new capability.
/// </summary>
public interface IAssistantActionExecutor
{
    /// <summary>Gets the intents this executor handles.</summary>
    IReadOnlyCollection<AssistantIntent> Intents { get; }

    /// <summary>
    /// Runs the command. Implementations must check their own consent requirements before
    /// touching a system service and must never execute an arbitrary command line.
    /// </summary>
    Task<VoiceCommandResult> ExecuteAsync(
        VoiceCommand command,
        CancellationToken cancellationToken = default);
}
