using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Core.Abstractions.AI;

/// <summary>
/// Represents a single AI provider and its conversational capabilities.
/// </summary>
public interface IAIProvider
{
    /// <summary>Gets the display name of the provider.</summary>
    string Name { get; }

    /// <summary>Gets the provider type identifier.</summary>
    AIProviderType ProviderType { get; }

    /// <summary>Gets a value indicating whether the provider is currently usable.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Sends a conversation history to the provider and returns the assistant response.
    /// </summary>
    Task<AIResponse> SendMessageAsync(
        IReadOnlyCollection<AIMessage> messages,
        CancellationToken cancellationToken = default);
}