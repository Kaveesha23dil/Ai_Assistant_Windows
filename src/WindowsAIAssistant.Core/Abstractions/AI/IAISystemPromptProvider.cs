namespace WindowsAIAssistant.Core.Abstractions.AI;

/// <summary>
/// Supplies the system instructions the assistant answers under.
/// <para>
/// The prompt is a separate abstraction because it describes what this assistant is allowed to
/// be, and that is a product decision rather than a property of any vendor. A provider
/// implementation therefore never writes prompt text of its own: it is handed the prompt and
/// sends it. Adding a provider cannot quietly change the assistant's behaviour.
/// </para>
/// </summary>
public interface IAISystemPromptProvider
{
    /// <summary>Gets the system instructions to place at the start of a conversation.</summary>
    ValueTask<string> GetSystemPromptAsync(CancellationToken cancellationToken = default);
}
