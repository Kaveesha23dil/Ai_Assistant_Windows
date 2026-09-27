namespace WindowsAIAssistant.Infrastructure.Configuration.Options;

/// <summary>
/// The AI configuration, bound from the <c>AI</c> section.
/// <para>
/// There is deliberately no key, endpoint, or organization field here. A credential is
/// something a person sets in their environment, not a value a configuration file can hold:
/// anything written here would be committed, copied into backups, and read by every other
/// process running as them. The only credential-related setting is the <em>name</em> of the
/// environment variable to read, so the same code can be pointed at a different store later
/// without editing a provider.
/// </para>
/// <para>
/// The provider name is free text on purpose, and an unrecognised value is a reported
/// setting rather than a refused start. A person who mistypes it is told what to fix instead
/// of being met with a start-up failure.
/// </para>
/// </summary>
public sealed class AIOptions
{
    public const string SectionName = "AI";

    /// <summary>
    /// Gets the provider to use: <c>Mock</c>, <c>OpenAI</c>, <c>Local</c>, or another name a
    /// later step registers. Defaults to the development provider, so a first run works with
    /// no account, no key, and no network.
    /// </summary>
    public string Provider { get; init; } = "Mock";

    /// <summary>
    /// Gets the model identifier sent to the provider. The default suits the development
    /// provider; a cloud provider needs one that exists in the account it is pointed at.
    /// </summary>
    public string Model { get; init; } = "mock-model";

    /// <summary>Gets the sampling temperature, from 0.0 for the most predictable answer to 2.0.</summary>
    public double Temperature { get; init; } = 0.7;

    /// <summary>Gets the ceiling on the answer's length, in tokens.</summary>
    public int MaxOutputTokens { get; init; } = 2048;

    /// <summary>Gets how long a single request may take before it is abandoned.</summary>
    public int RequestTimeoutSeconds { get; init; } = 60;

    /// <summary>
    /// Gets a value indicating whether answers arrive as they are written rather than all at
    /// once. Streaming is cosmetic in latency terms but it is what lets a person stop an
    /// answer they did not want.
    /// </summary>
    public bool UseStreaming { get; init; }

    /// <summary>
    /// Gets how many earlier messages are sent along with a new one. Bounding this keeps a
    /// long conversation from being resent in full on every turn.
    /// </summary>
    public int MaxConversationMessages { get; init; } = 20;

    /// <summary>
    /// Gets the name of the environment variable holding the API key for the selected cloud
    /// provider. Only the name lives in configuration; the value never does.
    /// </summary>
    public string ApiKeyEnvironmentVariable { get; init; } = "OPENAI_API_KEY";

    /// <summary>
    /// Gets instructions that replace the built-in system prompt. Empty means the built-in
    /// one, which is the safe default: it is the wording that says the assistant cannot run
    /// anything.
    /// </summary>
    public string? SystemPrompt { get; init; }
}
