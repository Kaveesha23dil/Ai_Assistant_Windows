using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.App.ViewModels;

/// <summary>
/// Presentation state shared by the pages that expose voice input.
/// <para>
/// The voice pipeline already owns an explicit lifecycle, so this type deliberately does not
/// keep its own "is listening" flag. It projects the single state the assistant publishes,
/// which is what stops the two from drifting apart.
/// </para>
/// <para>
/// Recognizer events arrive on the thread that owns the audio device, so every update is
/// marshalled to the dispatcher captured at construction. Mutating bound properties from that
/// thread would otherwise throw.
/// </para>
/// </summary>
public abstract partial class VoiceInteractionViewModel : ObservableObject, IDisposable
{
    private readonly IVoiceAssistantService _voice;
    private readonly DispatcherQueue _dispatcher;
    private bool _disposed;

    protected VoiceInteractionViewModel(IVoiceAssistantService voice)
    {
        ArgumentNullException.ThrowIfNull(voice);

        _voice = voice;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        IsVoiceEnabled = voice.IsEnabled;
        VoiceStatus = Describe(voice.State);
        LastResponse = voice.LastResponse;

        voice.StateChanged += OnStateChanged;
        voice.TranscriptUpdated += OnTranscriptUpdated;
        voice.ResponseProduced += OnResponseProduced;
    }

    /// <summary>Gets a value indicating whether voice may be used at all.</summary>
    [ObservableProperty]
    public partial bool IsVoiceEnabled { get; private set; }

    /// <summary>Gets the current state as text, so the page needs no enum formatting logic.</summary>
    [ObservableProperty]
    public partial string VoiceStatus { get; private set; }

    /// <summary>Gets the transcript captured so far, updated while the user speaks.</summary>
    [ObservableProperty]
    public partial string LiveTranscript { get; private set; } = string.Empty;

    /// <summary>Gets the most recent spoken or shown response.</summary>
    [ObservableProperty]
    public partial string? LastResponse { get; private set; }

    /// <summary>Gets a value indicating whether the microphone is open right now.</summary>
    [ObservableProperty]
    public partial bool IsListening { get; private set; }

    /// <summary>Gets a value indicating whether a command is being recognized or executed.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    /// <summary>Gets a value indicating whether a partial transcript is worth showing.</summary>
    public bool HasLiveTranscript => !string.IsNullOrWhiteSpace(LiveTranscript);

    /// <summary>Gets a value indicating whether there is a response to show.</summary>
    public bool HasLastResponse => !string.IsNullOrWhiteSpace(LastResponse);

    /// <summary>Gets the text shown when the user has not enabled voice.</summary>
    public string VoiceUnavailableMessage =>
        "Voice is off. Turn it on in Settings, then allow microphone access.";

    /// <summary>
    /// Gets a value indicating whether the voice feature is unavailable, which the pages bind
    /// to in order to show the explanation instead of the controls.
    /// </summary>
    public bool IsVoiceUnavailable => !IsVoiceEnabled;

    public bool CanStartListening => IsVoiceEnabled && !IsListening && !IsBusy;

    public bool CanStopListening => IsVoiceEnabled && IsListening;

    public bool CanCancelVoice => IsVoiceEnabled && (IsListening || IsBusy);

    partial void OnLiveTranscriptChanged(string value) =>
        OnPropertyChanged(nameof(HasLiveTranscript));

    partial void OnLastResponseChanged(string? value) =>
        OnPropertyChanged(nameof(HasLastResponse));

    partial void OnIsListeningChanged(bool value) => RefreshCommandStates();

    partial void OnIsBusyChanged(bool value) => RefreshCommandStates();

    partial void OnIsVoiceEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(IsVoiceUnavailable));
        RefreshCommandStates();
    }

    [RelayCommand(CanExecute = nameof(CanStartListening))]
    private async Task StartListeningAsync()
    {
        await RunAsync(() => _voice.StartListeningAsync()).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanStopListening))]
    private async Task StopListeningAsync()
    {
        await RunAsync(() => _voice.StopListeningAsync()).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanCancelVoice))]
    private async Task CancelVoiceAsync()
    {
        await RunAsync(() => _voice.CancelAsync()).ConfigureAwait(true);
    }

    /// <summary>
    /// Reports an operation that returned a failure result. Failures are shown as status text
    /// rather than thrown, because a denied microphone or a refused permission is an ordinary
    /// outcome the user needs to read, not a crash.
    /// </summary>
    private async Task RunAsync(Func<Task<Result>> operation)
    {
        var result = await operation().ConfigureAwait(true);

        if (result.IsFailure)
        {
            VoiceStatus = result.ErrorMessage ?? "The voice request was refused.";
        }
    }

    private void OnStateChanged(object? sender, VoiceStateChangedEventArgs e) =>
        Post(() => Apply(e.Current));

    private void OnTranscriptUpdated(object? sender, VoiceTranscriptEventArgs e) =>
        Post(() =>
        {
            LiveTranscript = e.Result.Text;

            // A settled transcript has been handed to the pipeline; keeping it on screen would
            // imply the assistant is still listening.
            if (e.IsFinal)
            {
                IsListening = false;
            }
        });

    private void OnResponseProduced(object? sender, VoiceResponseEventArgs e) =>
        Post(() => LastResponse = e.Result.ResponseText);

    private void Apply(VoiceAssistantState state)
    {
        IsVoiceEnabled = _voice.IsEnabled;
        IsListening = state == VoiceAssistantState.Listening;
        IsBusy = state is VoiceAssistantState.Processing or VoiceAssistantState.Executing;
        VoiceStatus = Describe(state);
    }

    private void RefreshCommandStates()
    {
        OnPropertyChanged(nameof(CanStartListening));
        OnPropertyChanged(nameof(CanStopListening));
        OnPropertyChanged(nameof(CanCancelVoice));
        StartListeningCommand.NotifyCanExecuteChanged();
        StopListeningCommand.NotifyCanExecuteChanged();
        CancelVoiceCommand.NotifyCanExecuteChanged();
    }

    private static string Describe(VoiceAssistantState state) => state switch
    {
        VoiceAssistantState.Disabled => "Voice is off",
        VoiceAssistantState.Idle => "Ready when you are",
        VoiceAssistantState.Listening => "Listening...",
        VoiceAssistantState.Processing => "Working out what you said",
        VoiceAssistantState.Executing => "Carrying that out",
        VoiceAssistantState.Speaking => "Speaking",
        VoiceAssistantState.Cancelled => "Cancelled",
        VoiceAssistantState.Error => "Something went wrong",
        _ => "Ready when you are"
    };

    /// <summary>
    /// Marshals a change onto the dispatcher when the event did not already arrive there. The
    /// queue can be shut down during teardown, in which case the update is dropped rather than
    /// allowed to throw on the recognizer thread.
    /// </summary>
    private void Post(Action action)
    {
        if (_dispatcher.HasThreadAccess)
        {
            action();
            return;
        }

        if (!_dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                if (!_disposed)
                {
                    action();
                }
            }))
        {
            return;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _voice.StateChanged -= OnStateChanged;
        _voice.TranscriptUpdated -= OnTranscriptUpdated;
        _voice.ResponseProduced -= OnResponseProduced;

        GC.SuppressFinalize(this);
    }
}
