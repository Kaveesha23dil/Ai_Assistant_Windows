namespace WindowsAIAssistant.Infrastructure.Configuration.Options;

/// <summary>
/// User-controlled consent switches, bound from the <c>Privacy</c> configuration section.
/// <para>
/// These are the switches every voice handler consults before touching a system service, so a
/// capability is never implicitly granted. Anything that reads data the user did not offer,
/// keeps the microphone open, or mutates system state starts off; only plainly harmless
/// actions such as launching an allow-listed application or opening a search page are on.
/// </para>
/// </summary>
public sealed class PrivacyOptions
{
    public const string SectionName = "Privacy";

    public bool AllowCloudAI { get; init; }

    /// <summary>
    /// Gets a value indicating whether a document's contents may be sent to a cloud provider.
    /// <para>
    /// Off unless it is deliberately turned on, and separate from
    /// <see cref="AllowCloudAI"/> on purpose: allowing a typed question to reach a cloud
    /// provider is a smaller decision than allowing a whole document to, and one person's
    /// answer to "may I use the cloud" should not silently answer the other question too.
    /// </para>
    /// </summary>
    public bool AllowDocumentCloudProcessing { get; init; }

    /// <summary>
    /// Gets a value indicating whether documents may be indexed into a local knowledge base so
    /// questions can be answered across all of them. On by default: reading, splitting, and
    /// storing text on this machine sends nothing anywhere.
    /// </summary>
    public bool AllowKnowledgeBase { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether document text may be sent to a cloud service to be turned
    /// into an embedding.
    /// <para>
    /// Off by default, and deliberately not implied by either other cloud permission. This is the
    /// step that posts every passage of every indexed document to a third party as a vector: a
    /// step somebody consenting to cloud chat, or to sending a document to be analyzed, has not
    /// agreed to. A vector is a lossy summary of the text behind it, and a party holding a few
    /// hundred of somebody's knows a great deal about their documents.
    /// </para>
    /// </summary>
    public bool AllowCloudEmbedding { get; init; }

    /// <summary>
    /// Gets a value indicating whether text retrieved from the knowledge base may be sent to a
    /// cloud AI provider to be answered.
    /// <para>
    /// Off by default, and required even when the embeddings were made on this machine, because
    /// this is the step that reaches the model rather than the step that indexed the document.
    /// </para>
    /// </summary>
    public bool AllowCloudKnowledgeProcessing { get; init; }

    public bool AllowTelemetry { get; init; }

    public bool AllowClipboardProcessing { get; init; }

    public bool AllowFileIndexing { get; init; }

    public bool AllowScreenAnalysis { get; init; }

    public bool StoreConversationHistory { get; init; }

    /// <summary>
    /// Gets a value indicating whether the assistant may open the microphone. Off until the
    /// user grants it, which is what keeps the microphone closed by default.
    /// </summary>
    public bool AllowMicrophoneAccess { get; init; }

    /// <summary>
    /// Gets a value indicating whether audio may be processed at all, locally or in the
    /// cloud. This gates every voice capability and is separate from microphone access so the
    /// microphone can be released without forgetting the user's preference.
    /// </summary>
    public bool AllowVoiceProcessing { get; init; }

    /// <summary>
    /// Gets a value indicating whether speech recognition may be performed by a cloud
    /// engine. Off by default: the local Windows recognizer is used instead.
    /// </summary>
    public bool AllowCloudSpeechProcessing { get; init; }

    /// <summary>Gets a value indicating whether screenshot capture is permitted.</summary>
    public bool AllowScreenCapture { get; init; }

    /// <summary>
    /// Gets a value indicating whether the assistant may change system-level state such as
    /// output volume or mute. Off by default because the effect is immediate and global.
    /// </summary>
    public bool AllowSystemControl { get; init; }

    /// <summary>
    /// Gets a value indicating whether the assistant may launch applications and open shell
    /// addresses. On by default because launching is restricted to an allow list.
    /// </summary>
    public bool AllowApplicationLaunch { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the assistant may run a web search and open it in the
    /// default browser. On by default; the query is URL-encoded and never reaches a shell.
    /// </summary>
    public bool AllowWebSearch { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether voice command history is retained in memory.
    /// The retained entries hold the intent and outcome only, never the transcript.
    /// </summary>
    public bool StoreVoiceHistory { get; init; }
}
