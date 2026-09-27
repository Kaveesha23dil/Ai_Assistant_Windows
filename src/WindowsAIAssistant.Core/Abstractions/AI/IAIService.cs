using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Core.Abstractions.AI;

/// <summary>
/// Provides asynchronous AI conversation capabilities independent of any specific provider.
/// <para>
/// This is the single seam every caller goes through. The typed chat page, the voice question
/// handler, and the clipboard summariser all depend on this interface and nothing else, so
/// there is exactly one place where a request is prepared, the consent switch is applied, the
/// timeout is enforced, and a provider failure is turned into something safe to show.
/// </para>
/// </summary>
public interface IAIService
{
    /// <summary>
    /// Gets the provider the service will use, or <see cref="AIProviderType.Unknown"/> when
    /// the configured provider is not one this build can resolve.
    /// </summary>
    AIProviderType ActiveProvider { get; }

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

    /// <summary>
    /// Sends a request and reports the answer as it arrives.
    /// <para>
    /// The sequence always ends with exactly one <see cref="AIStreamUpdateKind.Completed"/> or
    /// <see cref="AIStreamUpdateKind.Failed"/> update, so a caller can tell a finished answer
    /// from an interrupted one without tracking state itself.
    /// </para>
    /// </summary>
    IAsyncEnumerable<AIStreamUpdate> StreamMessageAsync(
        AIRequest request,
        CancellationToken cancellationToken = default);
}
