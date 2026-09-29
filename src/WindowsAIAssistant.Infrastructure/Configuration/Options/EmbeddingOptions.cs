namespace WindowsAIAssistant.Infrastructure.Configuration.Options;

/// <summary>
/// The embedding configuration, bound from the <c>Embeddings</c> section.
/// <para>
/// There is no key here, for the same reason <see cref="AIOptions"/> has none: a credential
/// belongs in a person's environment rather than in a file that gets committed, copied into
/// backups, and read by every other process running as them. The one credential-related value
/// is the name of the environment variable to read, and the key itself arrives through the same
/// <c>IAIApiKeyProvider</c> the chat path already uses.
/// </para>
/// <para>
/// The model is deliberately left empty. Each provider has its own embedding models with their
/// own widths, and naming one here would be a second place to change when switching providers —
/// and a place where a stale name fails only at the moment a document is being indexed, which is
/// the most expensive possible moment to discover it.
/// </para>
/// </summary>
public sealed class EmbeddingOptions
{
    public const string SectionName = "Embeddings";

    /// <summary>
    /// Gets the provider to use: <c>Mock</c> or <c>OpenAI</c>. Defaults to the development
    /// provider, so a first run works with no account, no key, and no network.
    /// </summary>
    public string Provider { get; init; } = nameof(Core.Enums.EmbeddingProviderKind.Mock);

    /// <summary>
    /// Gets the embedding model identifier, or empty to let the provider choose. The chosen model
    /// is written into the index beside the vectors it produced, so a document indexed with one
    /// model is never compared against a question embedded with another.
    /// </summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>
    /// Gets how many texts are sent to the provider in one request.
    /// <para>
    /// Not an optimization. Providers charge and rate-limit per request, so a document indexed
    /// one chunk at a time is both markedly slower and markedly more expensive than the same
    /// document indexed in batches, and neither shows up until somebody indexes a real file.
    /// </para>
    /// </summary>
    public int BatchSize { get; init; } = 32;

    /// <summary>
    /// Gets the most text sent for one embedding, in characters. A passage longer than this is
    /// cut, because a provider that refuses an over-long input would otherwise fail the whole
    /// document rather than one passage.
    /// </summary>
    public int MaximumInputCharacters { get; init; } = 12_000;

    /// <summary>
    /// Gets a value indicating whether vectors are scaled to unit length before they are stored.
    /// <para>
    /// On by default, and it is the difference between a search being a dot product over values
    /// that are already unit length and it being three times the arithmetic. Normalizing during
    /// every comparison instead would re-derive a thousand identical facts for every question.
    /// </para>
    /// </summary>
    public bool NormalizeVectors { get; init; } = true;

    /// <summary>
    /// Gets the name of the environment variable holding the API key for the selected provider.
    /// Only the name lives in configuration; the value never does.
    /// </summary>
    public string ApiKeyEnvironmentVariable { get; init; } = "OPENAI_API_KEY";
}
