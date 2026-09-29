using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Embeddings;

namespace WindowsAIAssistant.Core.Abstractions.Embeddings;

/// <summary>
/// Produces the vector for one piece of text.
/// <para>
/// A provider, rather than a service, because there is more than one and the choice is
/// configuration. It is a separate interface from <see cref="IEmbeddingService"/> for the same
/// reason <see cref="AI.IAIProvider"/> is separate from <see cref="AI.IAIService"/>: the
/// provider knows how to talk to one backend and nothing else, while the service owns batching,
/// truncation, the configured model, the consent check, and the error a person sees. Putting
/// either half in the other would mean a local model had to reimplement batching and an OpenAI
/// client had to reimplement consent.
/// </para>
/// <para>
/// The shape mirrors <see cref="AI.IAIProvider"/> deliberately, and the OpenAI SDK's own
/// <c>EmbeddingClient</c> already satisfies the community's
/// <c>IEmbeddingGenerator&lt;string, Embedding&lt;float&gt;&gt;</c> contract, so an adapter is a
/// few lines rather than a rewrite. That matters most for the provider that does not exist yet:
/// an ONNX model on this machine becomes an <see cref="IEmbeddingProvider"/> without any caller
/// above this interface changing.
/// </para>
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>Gets the provider's name, as written in configuration.</summary>
    string Name { get; }

    /// <summary>Gets which provider this is.</summary>
    EmbeddingProviderKind ProviderKind { get; }

    /// <summary>
    /// Gets the model this provider will use when configuration does not name one, or
    /// <see langword="null"/> when even the provider cannot say.
    /// <para>
    /// Not a convenience. The stored space of a vector records the model that produced it, and a
    /// reindex has to decide whether a stored vector may be reused — which it may only do if it
    /// knows the model in use. A provider that left this unknown would make every reindex
    /// re-embed every passage, and for a cloud provider that is the difference between a reindex
    /// and a bill.
    /// </para>
    /// </summary>
    string? DefaultModel { get; }

    /// <summary>
    /// Gets a value indicating whether text sent to this provider leaves this machine.
    /// <para>
    /// This is the fact the cloud-embedding permission is checked against, so it is
    /// deliberately pessimistic: a provider this build does not recognize counts as remote,
    /// because being wrong that way refuses a request that could have been allowed, while being
    /// wrong the other way posts a document's text to a service having asked permission to keep
    /// it local.
    /// </para>
    /// </summary>
    bool IsCloudHosted { get; }

    /// <summary>
    /// Gets a value indicating whether the provider is ready to be called: for a cloud provider,
    /// that means a credential exists.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Gets how many values a vector from this provider has, or <see langword="null"/> when the
    /// width is not known until a first vector arrives.
    /// <para>
    /// The knowledge base stores the width with each vector either way, so this is a
    /// convenience for reporting rather than something correctness depends on.
    /// </para>
    /// </summary>
    int? KnownDimensions { get; }

    /// <summary>Generates the vector for one piece of text.</summary>
    /// <param name="text">The text to embed. Never logged, and never retained by the provider.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    Task<EmbeddingVector> GenerateAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates vectors for several pieces of text in as few requests as the provider allows.
    /// <para>
    /// Batching is part of the contract rather than an optimization left to the caller. Embedding
    /// providers charge and rate-limit per request, so a hundred chunks sent one at a time is a
    /// hundred requests where one would do — which for a cloud provider is both slower and
    /// markedly more expensive, and would be discovered only after indexing a real document.
    /// </para>
    /// </summary>
    /// <param name="texts">The texts to embed, in order.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    Task<IReadOnlyList<EmbeddingVector>> GenerateBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
}
