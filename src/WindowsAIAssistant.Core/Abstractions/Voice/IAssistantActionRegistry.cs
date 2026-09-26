using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Abstractions.Voice;

/// <summary>
/// The set of executors available to the router, keyed by intent.
/// <para>
/// Registering every executor in the container is enough to build the table, so adding a
/// capability is a registration rather than an edit to a routing switch.
/// </para>
/// </summary>
public interface IAssistantActionRegistry
{
    /// <summary>Gets the intents that currently have an executor.</summary>
    IReadOnlyCollection<AssistantIntent> RegisteredIntents { get; }

    /// <summary>Attempts to find the executor for an intent.</summary>
    bool TryGetExecutor(AssistantIntent intent, out IAssistantActionExecutor? executor);
}
