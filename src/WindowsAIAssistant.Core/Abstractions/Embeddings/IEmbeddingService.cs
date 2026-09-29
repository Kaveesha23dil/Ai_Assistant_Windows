using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Embeddings;

namespace WindowsAIAssistant.Core.Abstractions.Embeddings;

/// <summary>
/// The one way anything in this application asks for an embedding.
/// <para>
/// Everything above this interface — indexing, retrieval, the knowledge page, the voice path —
/// depends on it and nothing else. The batching, the truncation, the configured model, the
/// consent decision, and the error a person sees all live behind it, which is what makes
/// "document text is never sent to a provider without permission" a property of the design
/// rather than a rule each caller has to remember.
/// </para>
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Gets the provider that will be used, or <see cref="EmbeddingProviderKind.Unknown"/> when
    /// the configured provider is not one this build can resolve.
    /// </summary>
    EmbeddingProviderKind ActiveProvider { get; }

    /// <summary>
    /// Gets the model the configured provider will be asked for, or <see langword="null"/> when
    /// the provider chooses for itself.
    /// <para>
    /// Read before indexing rather than discovered afterwards, because the model is half of the
    /// embedding space: if it changes, everything already indexed needs reindexing, and a person
    /// told that at the point they go looking for their documents is being told too late.
    /// </para>
    /// </summary>
    string? ActiveModel { get; }

    /// <summary>
    /// Gets the model that will actually be asked for, which is the configured one when there is
    /// one and the provider's own choice when there is not.
    /// <para>
    /// Separate from <see cref="ActiveModel"/> because "the configuration does not name a model"
    /// and "the model is unknown" are different facts: the first is normal and resolves to a
    /// value, and only the second is a problem. The compatibility check below needs the resolved
    /// name, so it is exposed rather than left for every caller to work out.
    /// </para>
    /// </summary>
    string? ResolvedModel { get; }

    /// <summary>
    /// Gets the space vectors from the active provider will live in, when it is already known.
    /// </summary>
    EmbeddingSpace? ActiveSpace { get; }

    /// <summary>
    /// Whether a stored space is the one this service is producing now, and so whether vectors
    /// recorded in it may be reused.
    /// <para>
    /// The width is deliberately not part of the answer. It belongs to the model, so two spaces
    /// with the same provider and model have the same width by definition, and the one case where
    /// that is not true — a provider whose model was changed by its owner without changing its
    /// name — cannot be detected from here at all.
    /// </para>
    /// </summary>
    bool IsCompatibleWith(EmbeddingSpace? space);

    /// <summary>
    /// Gets a value indicating whether the active provider runs on this machine, and so whether
    /// generating an embedding requires the person to have permitted it.
    /// </summary>
    bool IsLocalProvider { get; }

    /// <summary>
    /// Gets a value indicating whether the active provider is ready to be called. A cloud
    /// provider with no credential is not.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>Generates the vector for one piece of text.</summary>
    /// <param name="text">The text to embed. It is not logged, and it is not retained.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <exception cref="Exceptions.KnowledgeException">
    /// Thrown when the provider is missing a credential, the person has not permitted text to
    /// leave this machine, or the provider could not be reached.
    /// </exception>
    Task<EmbeddingVector> GenerateAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates vectors for several pieces of text, split into batches of the configured size.
    /// <para>
    /// The batching happens here rather than being left to the caller so that every path gets
    /// it: indexing a document, embedding a reindex, and embedding a question are three
    /// different callers, and a rate-limited provider meets them all or none.
    /// </para>
    /// </summary>
    /// <param name="texts">The texts to embed, in order.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    Task<IReadOnlyList<EmbeddingVector>> GenerateBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
}
