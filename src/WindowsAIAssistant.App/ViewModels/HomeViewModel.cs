using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.App.Services.Navigation;
using WindowsAIAssistant.Core.Abstractions.Navigation;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Infrastructure.Configuration.Options;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.App.ViewModels;

public sealed record HomeQuickAction(string Title, string Description, string Glyph, IRelayCommand Command);

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
    private readonly IAgentActivityStore? _activities;

    [ObservableProperty]
    public partial string DraftText { get; set; } = string.Empty;

    public HomeViewModel(
        IOptions<AIOptions> aiOptions,
        IOptions<PrivacyOptions> privacyOptions,
        INavigationService navigation,
        IVoiceAssistantService voice,
        IAgentActivityStore? activities = null)
        : base(voice)
    {
        ArgumentNullException.ThrowIfNull(aiOptions);
        ArgumentNullException.ThrowIfNull(privacyOptions);
        ArgumentNullException.ThrowIfNull(navigation);

        _navigation = navigation;
        _activities = activities;

        AiProvider = aiOptions.Value.Provider;
        PrivacyMode = privacyOptions.Value.AllowCloudAI ? "Cloud-enabled" : "Local-first";
        Greeting = BuildGreeting(DateTime.Now);
        QuickActions =
        [
            new("Start a conversation", "Think, write, and explore ideas with AI.", "\uE8F2", AskAiCommand),
            new("Find a file", "Search your folders by name, type, or date.", "\uE721", SearchFilesCommand),
            new("Read a document", "Get a summary or ask a focused question.", "\uE8A5", OpenDocumentsCommand),
            new("Your knowledge", "Find answers across your document library.", "\uE8F1", OpenKnowledgeCommand),
        ];
    }

    public string Greeting { get; }

    public string WelcomeMessage => "A little less busywork. A little more possibility.";
    public IReadOnlyList<HomeQuickAction> QuickActions { get; }
    public ObservableCollection<AgentActivity> RecentActivity { get; } = [];
    [ObservableProperty] public partial string ActivityMessage { get; set; } = "No agent activity yet. Your tasks will appear here.";
    public Visibility ActivityEmptyVisibility => RecentActivity.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public async Task RefreshActivityAsync()
    {
        if (_activities is null) return;
        try
        {
            var recent = await _activities.GetRecentAsync(3);
            RecentActivity.Clear();
            foreach (var activity in recent) RecentActivity.Add(activity);
        }
        catch (Exception) { ActivityMessage = "Recent activity is unavailable. Open the Agent workspace to try again."; }
        OnPropertyChanged(nameof(ActivityEmptyVisibility));
    }

    [RelayCommand]
    private void OpenDocuments() => _navigation.Navigate(NavigationRoute.Document);
    [RelayCommand]
    private void OpenKnowledge() => _navigation.Navigate(NavigationRoute.Knowledge);
    [RelayCommand]
    private void OpenAgent() => _navigation.Navigate(NavigationRoute.Agent);

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
        _navigation.Navigate(NavigationRoute.Clipboard);
    }

    private static string BuildGreeting(DateTime now) => now.Hour switch
    {
        < 12 => "Good morning",
        < 18 => "Good afternoon",
        _ => "Good evening"
    };
}
