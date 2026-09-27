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
}

/// <summary>The privacy switches, matching <c>Privacy</c> configuration.</summary>
public sealed class PrivacySettings
{
    public bool AllowCloudAI { get; set; }

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
