using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.App.ViewModels;

/// <summary>
/// Settings page state. Values are seeded from configuration and edited in memory only;
/// persistence is delivered by a later step. Privacy toggles mirror the safe defaults that
/// are already enforced elsewhere in the application.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    public partial bool IsLaunchAtStartupEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsSystemTrayIconEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsNotificationsEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsCloudAiEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsLocalAiEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsClipboardProcessingEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsFileIndexingEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsScreenAnalysisEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsConversationHistoryEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsTelemetryEnabled { get; set; }

    [ObservableProperty]
    public partial int SelectedThemeIndex { get; set; }

    [ObservableProperty]
    public partial bool IsVoiceEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsMicrophoneAccessEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsVoiceProcessingEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsSpokenResponsesEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsContinuousListeningEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsConfirmSensitiveActionsEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsCloudSpeechProcessingEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsScreenCaptureEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsSystemControlEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsWakeWordEnabled { get; set; }

    public SettingsViewModel(
        IOptions<AIOptions> aiOptions,
        IOptions<FeatureOptions> featureOptions,
        IOptions<PrivacyOptions> privacyOptions,
        IOptions<UIOptions> uiOptions,
        IOptions<VoiceOptions> voiceOptions)
    {
        ArgumentNullException.ThrowIfNull(aiOptions);
        ArgumentNullException.ThrowIfNull(featureOptions);
        ArgumentNullException.ThrowIfNull(privacyOptions);
        ArgumentNullException.ThrowIfNull(uiOptions);
        ArgumentNullException.ThrowIfNull(voiceOptions);

        AiProvider = aiOptions.Value.Provider;
        AiModel = aiOptions.Value.Model;
        IsLocalAiEnabled = featureOptions.Value.EnableLocalAI;
        IsCloudAiEnabled = privacyOptions.Value.AllowCloudAI;
        IsClipboardProcessingEnabled = privacyOptions.Value.AllowClipboardProcessing;
        IsFileIndexingEnabled = privacyOptions.Value.AllowFileIndexing;
        IsScreenAnalysisEnabled = privacyOptions.Value.AllowScreenAnalysis;
        IsConversationHistoryEnabled = privacyOptions.Value.StoreConversationHistory;
        IsTelemetryEnabled = privacyOptions.Value.AllowTelemetry;
        IsSystemTrayIconEnabled = uiOptions.Value.ShowSystemTrayIcon;

        var voice = voiceOptions.Value;
        var privacy = privacyOptions.Value;

        IsVoiceEnabled = voice.Enabled;
        IsSpokenResponsesEnabled = voice.SpeakResponses;
        IsContinuousListeningEnabled = voice.ContinuousListening;
        IsConfirmSensitiveActionsEnabled = voice.ConfirmSensitiveActions;
        IsWakeWordEnabled = voice.WakeWordEnabled;

        IsMicrophoneAccessEnabled = privacy.AllowMicrophoneAccess;
        IsVoiceProcessingEnabled = privacy.AllowVoiceProcessing;
        IsCloudSpeechProcessingEnabled = privacy.AllowCloudSpeechProcessing;
        IsScreenCaptureEnabled = privacy.AllowScreenCapture;
        IsSystemControlEnabled = privacy.AllowSystemControl;

        SelectedThemeIndex = ThemeOptions.ToList()
            .FindIndex(option => string.Equals(option, uiOptions.Value.Theme, StringComparison.OrdinalIgnoreCase));
        if (SelectedThemeIndex < 0)
        {
            SelectedThemeIndex = 0;
        }
    }

    public IReadOnlyList<string> ThemeOptions { get; } = ["System", "Light", "Dark"];

    public string ApplicationName => "Windows AI Assistant";

    public string Version => "0.1.0";

    public string VersionDisplay => $"Version {Version}";

    public string Platform => "Built for Windows 11";

    public string AiProvider { get; }

    public string AiModel { get; }

    public string Theme => ThemeOptions[SelectedThemeIndex];

    public string PersistenceNotice => "Changes apply to this session only. Saved preferences arrive in a later step.";

    public string PrivacyNotice => "These capabilities are permission controlled. Clipboard, file indexing, screen analysis, cloud AI and telemetry stay off until you turn them on here and the matching feature step is implemented.";

    public string VoiceNotice =>
        "The microphone is closed by default and only opens while you ask a question. Recognition runs on this device; cloud speech is off unless you allow it.";

    /// <summary>
    /// Gets a value indicating whether the wake phrase can be armed. It cannot yet, because no
    /// local wake-word engine is selected, and the control stays visible and disabled so the
    /// gap is obvious rather than hidden.
    /// </summary>
    public bool IsWakeWordSupported => false;

    public string WakeWordNotice =>
        "Wake word detection is unavailable: no local engine is installed, and no cloud alternative is offered.";

    partial void OnSelectedThemeIndexChanged(int value) => OnPropertyChanged(nameof(Theme));
}
