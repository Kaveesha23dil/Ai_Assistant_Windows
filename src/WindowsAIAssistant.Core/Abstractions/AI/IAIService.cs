using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Core.Abstractions.AI;

/// <summary>
/// Provides asynchronous AI conversation capabilities independent of any specific provider.
/// </summary>
public interface IAIService
{
    /// <summary>
    /// Sends a conversation history to the AI service and returns the assistant response.
    /// </summary>
    Task<AIResponse> SendMessageAsync(
        IReadOnlyCollection<AIMessage> messages,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a single message to the AI service and returns the assistant response.
    /// </summary>
    Task<AIResponse> SendMessageAsync(
        string message,
        CancellationToken cancellationToken = default);
}