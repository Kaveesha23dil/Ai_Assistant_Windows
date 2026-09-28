using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;
using WindowsAIAssistant.App.Services;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.App.ViewModels;

/// <summary>
/// Settings page state. Values are seeded from configuration, and saving writes them back to
/// the person's own settings file and applies them to the running session.
/// <para>
/// Privacy toggles mirror the safe defaults that are already enforced elsewhere in the
/// application, and a toggle with nothing behind it is shown disabled rather than pretending
/// to be saved.
/// </para>
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly UserSettingsService _userSettings;
    private string _saveStatus = "Changes are not saved until you save them.";

    [ObservableProperty]
    public partial bool IsLaunchAtStartupEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsSystemTrayIconEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsNotificationsEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsCloudAiEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether documents may be sent to a cloud provider.
    /// Deliberately a second switch rather than part of the cloud-AI one, and off regardless of
    /// what the cloud-AI switch says.
    /// </summary>
    [ObservableProperty]
    public partial bool IsDocumentCloudProcessingEnabled { get; set; }

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

    /// <summary>
    /// Gets or sets which provider is chosen. An index rather than a name, because the page
    /// offers the names as a list and a name would let the two drift apart.
    /// </summary>
    [ObservableProperty]
    public partial int SelectedProviderIndex { get; set; }

    /// <summary>
    /// Gets or sets the model name. This is editable because the set of models a provider
    /// offers changes faster than this application does: a person who knows the name their
    /// account has access to should be able to type it rather than wait for a list to catch up.
    /// </summary>
    [ObservableProperty]
    public partial string AiModelText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether answers arrive word by word. Turning it off makes
    /// an answer appear all at once, which is worth having for a slow connection.
    /// </summary>
    [ObservableProperty]
    public partial bool IsStreamingEnabled { get; set; }

    /// <summary>Gets a value indicating whether a save is in progress.</summary>
    [ObservableProperty]
    public partial bool IsSaving { get; private set; }

    /// <summary>
    /// Gets the outcome of the last save attempt, shown next to the button so the person is
    /// told whether their change was kept rather than left to guess.
    /// </summary>
    public string SaveStatus
    {
        get => _saveStatus;
        private set
        {
            if (string.Equals(_saveStatus, value, StringComparison.Ordinal))
            {
                return;
            }

            _saveStatus = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Gets a value indicating whether the save button can be pressed.</summary>
    public bool CanSave => !IsSaving;

    /// <summary>
    /// Gets whether the save progress ring is shown.
    /// <para>
    /// This is spelled out here rather than bound straight from <see cref="IsSaving"/> because a
    /// bound boolean cannot be turned into a <see cref="Visibility"/> on its own, and a page
    /// that asks for one anyway fails to load rather than merely looking wrong.
    /// </para>
    /// </summary>
    public Visibility SavingVisibility => IsSaving ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Saves the current settings.</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsSaving)
        {
            return;
        }

        IsSaving = true;
        SaveStatus = "Saving...";

        try
        {
            var result = await _userSettings.SaveAsync(ToUserSettings());
            SaveStatus = result.ErrorMessage
                ?? result.SuccessMessage
                ?? $"Saved to {_userSettings.Location}.";
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>
    /// Projects the page onto the persisted document. Only settings that something reads are
    /// included, so a saved file never claims to hold a preference nothing honours.
    /// </summary>
    private UserSettings ToUserSettings() => new()
    {
        Privacy = new PrivacySettings
        {
            AllowCloudAI = IsCloudAiEnabled,
            AllowDocumentCloudProcessing = IsDocumentCloudProcessingEnabled,
            AllowTelemetry = IsTelemetryEnabled,
            AllowClipboardProcessing = IsClipboardProcessingEnabled,
            AllowFileIndexing = IsFileIndexingEnabled,
            AllowScreenAnalysis = IsScreenAnalysisEnabled,
            StoreConversationHistory = IsConversationHistoryEnabled,
            AllowMicrophoneAccess = IsMicrophoneAccessEnabled,
            AllowVoiceProcessing = IsVoiceProcessingEnabled,
            AllowCloudSpeechProcessing = IsCloudSpeechProcessingEnabled,
            AllowScreenCapture = IsScreenCaptureEnabled,
            AllowSystemControl = IsSystemControlEnabled,
        },
        Voice = new VoiceSettings
        {
            Enabled = IsVoiceEnabled,
            SpeakResponses = IsSpokenResponsesEnabled,
            ContinuousListening = IsContinuousListeningEnabled,
            ConfirmSensitiveActions = IsConfirmSensitiveActionsEnabled,
        },
        UI = new UiSettings
        {
            Theme = Theme,
            ShowSystemTrayIcon = IsSystemTrayIconEnabled,
        },
        Features = new FeatureSettings
        {
            EnableLocalAI = IsLocalAiEnabled,
        },
        AI = new AISettings
        {
            Provider = SelectedProvider,
            Model = AiModelText?.Trim() ?? string.Empty,
            UseStreaming = IsStreamingEnabled,
        },
    };

    /// <param name="aiOptions">The AI options currently in effect.</param>
    /// <param name="featureOptions">The feature options currently in effect.</param>
    /// <param name="privacyOptions">The privacy options currently in effect.</param>
    /// <param name="uiOptions">The interface options currently in effect.</param>
    /// <param name="voiceOptions">The voice options currently in effect.</param>
    /// <param name="userSettings">The service that writes and applies a saved change.</param>
    /// <remarks>
    /// The options are monitors rather than snapshots on purpose. A snapshot is worked out once
    /// and then handed to every page for the life of the process, so coming back to this page
    /// after a save would show the values from sign-in and pressing save again would write those
    /// stale values back over the ones just saved. A monitor always reports the values that are
    /// in effect right now.
    /// </remarks>
    public SettingsViewModel(
        IOptionsMonitor<AIOptions> aiOptions,
        IOptionsMonitor<FeatureOptions> featureOptions,
        IOptionsMonitor<PrivacyOptions> privacyOptions,
        IOptionsMonitor<UIOptions> uiOptions,
        IOptionsMonitor<VoiceOptions> voiceOptions,
        UserSettingsService userSettings)
    {
        ArgumentNullException.ThrowIfNull(aiOptions);
        ArgumentNullException.ThrowIfNull(featureOptions);
        ArgumentNullException.ThrowIfNull(privacyOptions);
        ArgumentNullException.ThrowIfNull(uiOptions);
        ArgumentNullException.ThrowIfNull(voiceOptions);
        ArgumentNullException.ThrowIfNull(userSettings);

        _userSettings = userSettings;

        ApiKeyVariableName = aiOptions.CurrentValue.ApiKeyEnvironmentVariable;
        AiModelText = aiOptions.CurrentValue.Model ?? string.Empty;
        IsStreamingEnabled = aiOptions.CurrentValue.UseStreaming;
        SelectedProviderIndex = IndexOfProvider(aiOptions.CurrentValue.Provider);
        IsLocalAiEnabled = featureOptions.CurrentValue.EnableLocalAI;
        IsCloudAiEnabled = privacyOptions.CurrentValue.AllowCloudAI;
        IsDocumentCloudProcessingEnabled = privacyOptions.CurrentValue.AllowDocumentCloudProcessing;
        IsClipboardProcessingEnabled = privacyOptions.CurrentValue.AllowClipboardProcessing;
        IsFileIndexingEnabled = privacyOptions.CurrentValue.AllowFileIndexing;
        IsScreenAnalysisEnabled = privacyOptions.CurrentValue.AllowScreenAnalysis;
        IsConversationHistoryEnabled = privacyOptions.CurrentValue.StoreConversationHistory;
        IsTelemetryEnabled = privacyOptions.CurrentValue.AllowTelemetry;
        IsSystemTrayIconEnabled = uiOptions.CurrentValue.ShowSystemTrayIcon;

        var voice = voiceOptions.CurrentValue;
        var privacy = privacyOptions.CurrentValue;

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

        SelectedThemeIndex = ThemeOptions
            .ToList()
            .FindIndex(option => string.Equals(option, uiOptions.CurrentValue.Theme, StringComparison.OrdinalIgnoreCase));
        if (SelectedThemeIndex < 0)
        {
            SelectedThemeIndex = 0;
        }
    }

    public IReadOnlyList<string> ThemeOptions { get; } = ["System", "Light", "Dark"];

    /// <summary>
    /// Gets the providers that may be chosen, in the order they are offered. Read from the
    /// shared list rather than written out here, so the page cannot offer a provider that the
    /// save would then refuse.
    /// </summary>
    public IReadOnlyList<string> ProviderOptions => AIProviderTypes.SupportedNames;

    /// <summary>Gets the provider currently chosen.</summary>
    public string SelectedProvider => ProviderOptions[
        Math.Clamp(SelectedProviderIndex, 0, ProviderOptions.Count - 1)];

    /// <summary>
    /// Gets the name of the environment variable the cloud provider's key is read from, shown so
    /// a person knows where to put it. The key itself is never displayed, and is never
    /// something this page could display.
    /// </summary>
    public string ApiKeyVariableName { get; private set; } = "OPENAI_API_KEY";

    /// <summary>
    /// Gets the note shown under the provider choice. It differs by provider, because the one
    /// that needs nothing and the one that needs a key should not be described the same way.
    /// </summary>
    public string ProviderNotice =>
        SelectedProvider == nameof(AIProviderType.Mock)
            ? "Answers come from a built-in stand-in. No account and no key are needed, and nothing leaves this device."
            : $"Answers are sent to {SelectedProvider}. Set the {ApiKeyVariableName} environment variable first, and allow cloud AI below.";

    /// <summary>
    /// Gets a value indicating whether the chosen provider needs a key that has not been
    /// entered. Detection is not possible from here, so the field simply explains what is
    /// needed rather than claiming a state it cannot see.
    /// </summary>
    public bool RequiresApiKey => SelectedProvider != nameof(AIProviderType.Mock);

    public string ApplicationName => "Windows AI Assistant";

    public string Version => "0.1.0";

    public string VersionDisplay => $"Version {Version}";

    public string Platform => "Built for Windows 11";

    public string Theme => ThemeOptions[SelectedThemeIndex];

    public string PersistenceNotice =>
        "Settings are saved to your user profile and are read again the next time you open the app.";

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

    /// <summary>
    /// Gets a value indicating whether launching at sign-in can be changed. It cannot yet,
    /// because nothing registers a start-up task, so the control stays visible and disabled
    /// rather than offering a setting that would do nothing.
    /// </summary>
    public bool IsLaunchAtStartupSupported => false;

    public string LaunchAtStartupNotice =>
        "Starting with Windows is not implemented yet, so this setting is not saved.";

    /// <summary>
    /// Gets a value indicating whether notifications can be changed. They cannot yet, because
    /// no notification is raised anywhere in the application.
    /// </summary>
    public bool IsNotificationsSupported => false;

    public string NotificationsNotice =>
        "Nothing raises a notification yet, so this setting is not saved.";

    partial void OnSelectedThemeIndexChanged(int value) => OnPropertyChanged(nameof(Theme));

    partial void OnSelectedProviderIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedProvider));
        OnPropertyChanged(nameof(ProviderNotice));
        OnPropertyChanged(nameof(RequiresApiKey));
    }

    /// <summary>
    /// Finds where a configured provider sits in the list, falling back to the first entry so
    /// the page never opens with nothing selected. A name this build does not serve would
    /// otherwise leave the page showing a blank choice and saving a provider that cannot answer.
    /// </summary>
    private static int IndexOfProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return 0;
        }

        var index = AIProviderTypes.SupportedNames
            .ToList()
            .FindIndex(name => string.Equals(name, provider.Trim(), StringComparison.OrdinalIgnoreCase));

        return index < 0 ? 0 : index;
    }

    partial void OnIsSavingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(SavingVisibility));
    }
}
