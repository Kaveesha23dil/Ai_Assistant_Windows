using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.App.ViewModels;

/// <summary>
/// Landing page state. Values shown here are derived from configuration where possible and
/// never contain machine or account details.
/// </summary>
public sealed partial class HomeViewModel : VoiceInteractionViewModel
{
    [ObservableProperty]
    public partial string DraftText { get; set; } = string.Empty;

    public HomeViewModel(
        IOptions<AIOptions> aiOptions,
        IOptions<PrivacyOptions> privacyOptions,
        IVoiceAssistantService voice)
        : base(voice)
    {
        ArgumentNullException.ThrowIfNull(aiOptions);
        ArgumentNullException.ThrowIfNull(privacyOptions);

        AiProvider = aiOptions.Value.Provider;
        PrivacyMode = privacyOptions.Value.AllowCloudAI ? "Cloud-enabled" : "Local-first";
        Greeting = BuildGreeting(DateTime.Now);
    }

    public string Greeting { get; }

    public string WelcomeMessage => "Windows AI Assistant is ready. Features are being enabled step by step.";

    public string AiProvider { get; }

    public string AiStatus => "Ready";

    public string SystemStatus => "Ready";

    public string PrivacyMode { get; }

    public bool HasDraftText => !string.IsNullOrWhiteSpace(DraftText);

    partial void OnDraftTextChanged(string value) => OnPropertyChanged(nameof(HasDraftText));

    [RelayCommand]
    private void SubmitDraft()
    {
    }

    [RelayCommand]
    private void AskAi()
    {
    }

    [RelayCommand]
    private void SearchFiles()
    {
    }

    [RelayCommand]
    private void SystemInfo()
    {
    }

    [RelayCommand]
    private void ClipboardAssistant()
    {
    }

    private static string BuildGreeting(DateTime now) => now.Hour switch
    {
        < 12 => "Good morning",
        < 18 => "Good afternoon",
        _ => "Good evening"
    };
}
