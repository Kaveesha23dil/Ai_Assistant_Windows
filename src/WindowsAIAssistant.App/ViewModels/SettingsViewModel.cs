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

    public SettingsViewModel(
        IOptions<AIOptions> aiOptions,
        IOptions<FeatureOptions> featureOptions,
        IOptions<PrivacyOptions> privacyOptions,
        IOptions<UIOptions> uiOptions)
    {
        ArgumentNullException.ThrowIfNull(aiOptions);
        ArgumentNullException.ThrowIfNull(featureOptions);
        ArgumentNullException.ThrowIfNull(privacyOptions);
        ArgumentNullException.ThrowIfNull(uiOptions);

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

    partial void OnSelectedThemeIndexChanged(int value) => OnPropertyChanged(nameof(Theme));
}
