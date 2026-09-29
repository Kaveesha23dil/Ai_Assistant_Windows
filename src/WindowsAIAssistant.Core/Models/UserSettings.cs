namespace WindowsAIAssistant.Core.Models;

/// <summary>
/// The settings a person can change from the Settings page, in the shape they are written to
/// disk.
/// <para>
/// The property names are deliberately identical to the configuration section and option
/// names, because this document is read back as a configuration source. That means a saved
/// value and a value typed into appsettings.json travel the same path through the
/// application, so there is only ever one place where a setting is interpreted.
/// </para>
/// <para>
/// Only settings that a real option reads are represented. A toggle with nothing behind it
/// would be saved and then never consulted, which is worse than not offering it.
/// </para>
/// </summary>
public sealed class UserSettings
{
    public PrivacySettings Privacy { get; set; } = new();

    public VoiceSettings Voice { get; set; } = new();

    public UiSettings UI { get; set; } = new();

    public FeatureSettings Features { get; set; } = new();

    public AISettings AI { get; set; } = new();
}

/// <summary>
/// The AI choices a person can change, matching the <c>AI</c> configuration section.
/// <para>
/// There is no credential here, and there never will be. The saved document is read back as
/// configuration and copied into backups, so a key written into it would end up in places the
/// person did not choose. The key stays in the environment variable named by
/// <c>ApiKeyEnvironmentVariable</c>, and this document only says which one.
/// </para>
/// </summary>
public sealed class AISettings
{
    public string Provider { get; set; } = "Mock";

    public string Model { get; set; } = "mock-model";

    public bool UseStreaming { get; set; } = true;
}

/// <summary>The privacy switches, matching <c>Privacy</c> configuration.</summary>
public sealed class PrivacySettings
{
    public bool AllowCloudAI { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether document contents may be sent to a cloud
    /// provider. Off by default, and separate from <see cref="AllowCloudAI"/>: a question a
    /// person typed is not the same as a file they opened.
    /// </summary>
    public bool AllowDocumentCloudProcessing { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the assistant may keep a local index of documents
    /// so questions can be answered across all of them. On by default, and local work only.
    /// </summary>
    public bool AllowKnowledgeBase { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether document text may be sent to a cloud service to
    /// be turned into an embedding. Off by default, and independent of both other cloud
    /// permissions: answering a question is not the same decision as embedding a document.
    /// </summary>
    public bool AllowCloudEmbedding { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether text retrieved from the knowledge base may be
    /// sent to a cloud AI provider to be answered. Off by default, and required even when the
    /// embeddings were made on this machine, because this is the step that reaches the model.
    /// </summary>
    public bool AllowCloudKnowledgeProcessing { get; set; }

    public bool AllowTelemetry { get; set; }

    public bool AllowClipboardProcessing { get; set; }

    public bool AllowFileIndexing { get; set; }

    public bool AllowScreenAnalysis { get; set; }

    public bool StoreConversationHistory { get; set; }

    public bool AllowMicrophoneAccess { get; set; }

    public bool AllowVoiceProcessing { get; set; }

    public bool AllowCloudSpeechProcessing { get; set; }

    public bool AllowScreenCapture { get; set; }

    public bool AllowSystemControl { get; set; }

    public bool StoreVoiceHistory { get; set; }
}

/// <summary>The voice switches, matching <c>Voice</c> configuration.</summary>
public sealed class VoiceSettings
{
    public bool Enabled { get; set; }

    public bool SpeakResponses { get; set; } = true;

    public bool ContinuousListening { get; set; }

    public bool ConfirmSensitiveActions { get; set; } = true;
}

/// <summary>The appearance switches, matching <c>UI</c> configuration.</summary>
public sealed class UiSettings
{
    public string Theme { get; set; } = "System";

    public bool ShowSystemTrayIcon { get; set; } = true;
}

/// <summary>The feature flags, matching <c>Features</c> configuration.</summary>
public sealed class FeatureSettings
{
    public bool EnableLocalAI { get; set; }
}
