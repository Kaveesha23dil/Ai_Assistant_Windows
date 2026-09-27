using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Core.Abstractions.AI;

/// <summary>
/// Represents a single AI provider and its conversational capabilities.
/// <para>
/// Everything above this interface is provider-neutral on purpose. A provider is the only
/// place allowed to know which vendor it talks to, which keeps the choice a configuration
/// value rather than a compile-time decision.
/// </para>
/// </summary>
public interface IAIProvider
{
    /// <summary>Gets the display name of the provider.</summary>
    string Name { get; }

    /// <summary>Gets the provider type identifier.</summary>
    AIProviderType ProviderType { get; }

    /// <summary>
    /// Gets a value indicating whether the provider runs somewhere other than this machine.
    /// <para>
    /// This is what the consent check is built on: a request may only leave the device if the
    /// provider reports that it does, so a new cloud provider inherits the privacy switch
    /// instead of needing to remember it.
    /// </para>
    /// </summary>
    bool IsCloudHosted { get; }

    /// <summary>Gets a value indicating whether the provider is currently usable.</summary>
    bool IsAvailable { get; }

    /// <summary>Gets a value indicating whether the provider can produce updates as it answers.</summary>
    bool SupportsStreaming { get; }

    /// <summary>
    /// Sends a conversation to the provider and returns the whole answer at once.
    /// </summary>
    Task<AIResponse> SendMessageAsync(
        AIRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a conversation to the provider and reports the answer as it arrives.
    /// <para>
    /// A provider that does not support streaming still honours this: it yields a single
    /// <see cref="AIStreamUpdateKind.Completed"/> update, so a caller can always use the
    /// streamed path and never has to branch on the provider.
    /// </para>
    /// </summary>
    IAsyncEnumerable<AIStreamUpdate> StreamMessageAsync(
        AIRequest request,
        CancellationToken cancellationToken = default);
}
