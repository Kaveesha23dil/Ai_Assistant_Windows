using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.App.Services.Navigation;
using WindowsAIAssistant.Core.Abstractions.Navigation;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.App.ViewModels;

/// <summary>
/// Landing page state. Values shown here are derived from configuration where possible and
/// never contain machine or account details.
/// <para>
/// The quick actions are navigation only. Each one asks the navigation service for a page and
/// does nothing else, which means they behave identically whether they are clicked or the same
/// destination is requested by voice, and it leaves the actual chat, search, and settings work
/// to the pages that will own it.
/// </para>
/// </summary>
public sealed partial class HomeViewModel : VoiceInteractionViewModel
{
    private readonly INavigationService _navigation;

    [ObservableProperty]
    public partial string DraftText { get; set; } = string.Empty;

    public HomeViewModel(
        IOptions<AIOptions> aiOptions,
        IOptions<PrivacyOptions> privacyOptions,
        INavigationService navigation,
        IVoiceAssistantService voice)
        : base(voice)
    {
        ArgumentNullException.ThrowIfNull(aiOptions);
        ArgumentNullException.ThrowIfNull(privacyOptions);
        ArgumentNullException.ThrowIfNull(navigation);

        _navigation = navigation;

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

    /// <summary>Opens the conversation page.</summary>
    [RelayCommand]
    private void AskAi() => _navigation.Navigate(NavigationRoute.Chat);

    /// <summary>Opens the file search page.</summary>
    [RelayCommand]
    private void SearchFiles() => _navigation.Navigate(NavigationRoute.Files);

    /// <summary>Opens the automations page.</summary>
    [RelayCommand]
    private void OpenAutomations() => _navigation.Navigate(NavigationRoute.Automations);

    /// <summary>Opens this application's own settings page.</summary>
    [RelayCommand]
    private void OpenSettings() => _navigation.Navigate(NavigationRoute.Settings);

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
