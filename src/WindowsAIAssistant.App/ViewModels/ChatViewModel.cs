using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowsAIAssistant.Core.Abstractions.Voice;

namespace WindowsAIAssistant.App.ViewModels;

/// <summary>
/// Chat page state for the shell only. The conversation flow is intentionally not connected
/// to any AI service yet.
/// </summary>
public sealed partial class ChatViewModel : VoiceInteractionViewModel
{
    [ObservableProperty]
    public partial string DraftText { get; set; } = string.Empty;

    public ChatViewModel(IVoiceAssistantService voice)
        : base(voice)
    {
    }

    public string HeaderTitle => "Chat";

    public string EmptyStateTitle => "Start a conversation";

    public string EmptyStateMessage => "Ask the assistant anything.";

    public string PlaceholderText => "Ask anything...";

    public bool HasDraftText => !string.IsNullOrWhiteSpace(DraftText);

    public bool CanSend => HasDraftText;

    partial void OnDraftTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasDraftText));
        OnPropertyChanged(nameof(CanSend));
        SendCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private void Send()
    {
    }

    [RelayCommand]
    private void NewChat() => DraftText = string.Empty;
}
