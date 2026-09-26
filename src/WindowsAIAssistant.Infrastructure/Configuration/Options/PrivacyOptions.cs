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
