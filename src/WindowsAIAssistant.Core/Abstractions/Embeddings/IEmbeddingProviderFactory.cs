namespace WindowsAIAssistant.Core.Abstractions.Embeddings;

/// <summary>
/// Picks an embedding provider by name.
/// <para>
/// A factory rather than a service-level <c>switch</c> so that the choice between providers is
/// resolved from the container. Every provider is registered as an
/// <see cref="IEmbeddingProvider"/>, and this is the only place that knows a name maps to one —
/// the same arrangement the chat path uses, so the two features are extended the same way.
/// </para>
/// </summary>
public interface IEmbeddingProviderFactory
{
    /// <summary>
    /// Resolves a provider by the name in configuration, falling back to the development provider
    /// when the name is empty or is not one this build provides.
    /// <para>
    /// The fallback is deliberate and is documented on the implementation: a knowledge base is
    /// something somebody uses before they have finished configuring the assistant, and a
    /// misspelled provider name should cost them the quality of a search rather than the ability
    /// to ask a question at all.
    /// </para>
    /// </summary>
    IEmbeddingProvider Resolve(string? name);
}
