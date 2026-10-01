using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowsAIAssistant.Application.Agents;
using WindowsAIAssistant.Application.AI.Commands.SendMessage;
using WindowsAIAssistant.Application.AI.Services;
using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.App.ViewModels;

/// <summary>
/// The chat page: a transcript, a composer, and the state of the request in flight.
/// <para>
/// The page holds no provider knowledge. It asks the Application layer for an answer and shows
/// what comes back, which is why typing a question and asking one by voice behave identically:
/// both arrive through the same handlers, and the only difference the interface makes is
/// whether the answer arrives in pieces.
/// </para>
/// <para>
/// Every command here is written to leave the page in a state a person can act from. Sending
/// clears the draft and disables itself, a stop keeps whatever was already written, and a new
/// chat cancels an answer in flight before clearing the transcript, so an abandoned request
/// cannot keep appending to a conversation that is no longer on screen.
/// </para>
/// </summary>
public sealed partial class ChatViewModel : VoiceInteractionViewModel
{
    private readonly StreamMessageHandler _streamMessages;
    private readonly SendMessageHandler _sendMessages;
    private readonly IConversationService _conversations;
    private readonly IAIRequestDefaults _defaults;

    /// <summary>
    /// The engine behind the opt-in path, used only when somebody asks for the agent by name.
    /// </summary>
    private readonly IAgent _agent;

    /// <summary>
    /// Set when an answer is being written. It cancels the request when the person presses
    /// stop, and a new request replaces it rather than joining it, so two answers can never
    /// interleave into the same transcript.
    /// </summary>
    private CancellationTokenSource? _generation;

    /// <summary>
    /// Completes once the conversation exists. Every command waits for it, because a request
    /// against a conversation that has not been created yet would be recorded nowhere.
    /// </summary>
    private readonly Task _ready;

    private ChatMessageViewModel? _streamingMessage;
    private readonly StringBuilder _streamingText = new();
    private bool _disposed;

    public ChatViewModel(
        IVoiceAssistantService voice,
        StreamMessageHandler streamMessages,
        SendMessageHandler sendMessages,
        IConversationService conversations,
        IAIRequestDefaults defaults,
        IAgent agent)
        : base(voice)
    {
        ArgumentNullException.ThrowIfNull(streamMessages);
        ArgumentNullException.ThrowIfNull(sendMessages);
        ArgumentNullException.ThrowIfNull(conversations);
        ArgumentNullException.ThrowIfNull(defaults);
        ArgumentNullException.ThrowIfNull(agent);

        _streamMessages = streamMessages;
        _sendMessages = sendMessages;
        _conversations = conversations;
        _defaults = defaults;
        _agent = agent;

        // Started here rather than awaited from a command so a person who types immediately is
        // not refused: the commands wait for this instead.
        _ready = InitialiseConversationAsync();
    }

    /// <summary>Gets the transcript, oldest first.</summary>
    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    /// <summary>Gets the text the person is typing.</summary>
    [ObservableProperty]
    public partial string DraftText { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether an answer is being written.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStop))]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(StreamingVisibility))]
    public partial bool IsGenerating { get; private set; }

    /// <summary>
    /// Gets the text of the answer as it is written. The transcript shows the same text on the
    /// message itself; this is the single place that makes the growing answer visible without
    /// the message being bound twice.
    /// </summary>
    [ObservableProperty]
    public partial string CurrentStreamingText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the conversation this page is showing. It is created per page instance, which
    /// keeps the transcript in memory for the life of the page and no longer: the same promise
    /// the rest of the application makes about conversations, and the reason there is no
    /// history list to scroll.
    /// </summary>
    public Guid ConversationId { get; private set; }

    /// <summary>Gets the heading.</summary>
    public string HeaderTitle => "Chat";

    /// <summary>Gets the heading shown when nothing has been said yet.</summary>
    public string EmptyStateTitle => "Start a conversation";

    /// <summary>Gets the text shown when nothing has been said yet.</summary>
    public string EmptyStateMessage => "Ask anything. The conversation stays on this device.";

    /// <summary>Gets the composer placeholder.</summary>
    public string PlaceholderText => "Ask anything...";

    /// <summary>Gets a value indicating whether there is something to send.</summary>
    public bool HasDraftText => !string.IsNullOrWhiteSpace(DraftText);

    /// <summary>Gets a value indicating whether the transcript is empty.</summary>
    public bool IsEmpty => Messages.Count == 0;

    /// <summary>Gets a value indicating whether nothing is happening.</summary>
    public bool IsIdle => !IsGenerating;

    /// <summary>Gets a value indicating whether the send button is available.</summary>
    public bool CanSend => HasDraftText && !IsGenerating;

    /// <summary>Gets a value indicating whether the stop button is available.</summary>
    public bool CanStop => IsGenerating;

    /// <summary>
    /// Gets a value indicating whether the streaming indicator is shown, so the page needs no
    /// logic of its own to turn a bound boolean into a visibility.
    /// </summary>
    public Microsoft.UI.Xaml.Visibility StreamingVisibility => IsGenerating
        ? Microsoft.UI.Xaml.Visibility.Visible
        : Microsoft.UI.Xaml.Visibility.Collapsed;

    partial void OnDraftTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasDraftText));
        OnPropertyChanged(nameof(CanSend));
        SendCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Sends what is in the composer.
    /// <para>
    /// Enter sends and Shift+Enter inserts a newline, which the page wires up because a
    /// keyboard gesture is an interface concern. Both paths end up here, so they cannot
    /// disagree about what counts as a sendable message.
    /// </para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = DraftText?.Trim();
        if (string.IsNullOrWhiteSpace(text) || IsGenerating)
        {
            return;
        }

        await _ready.ConfigureAwait(true);

        DraftText = string.Empty;
        Append(ChatMessageViewModel.FromUser(text));

        var generation = BeginGeneration();
        try
        {
            if (AgentRequestTrigger.TryMatch(text, AgentRequestSource.Text, out var agentRequest))
            {
                await RunAgentAsync(agentRequest.WithConversation(ConversationId), generation.Token);
            }
            else if (_defaults.UseStreaming)
            {
                await StreamAsync(text, generation.Token);
            }
            else
            {
                await SendWholeAnswerAsync(text, generation.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // A stop is an ordinary ending, not a failure. Whatever had already been written is
            // still on screen and is kept, and a note says why the answer ended early so a
            // truncated transcript does not look like a fault.
            Publish();
            FinishStreaming();
            Append(ChatMessageViewModel.FromStatus("Stopped."));
        }
        catch (Exception)
        {
            // Every expected failure is already a response or a failure update carrying a
            // sentence written for a person. Reaching this point means something genuinely
            // unexpected happened, and the message says so without putting an exception or its
            // text in front of anybody.
            Publish();
            FinishStreaming();
            Append(ChatMessageViewModel.FromStatus(
                "The assistant could not complete that request. Try again in a moment."));
        }
        finally
        {
            EndGeneration(generation);
        }
    }

    /// <summary>
    /// Hands a request the trigger recognised to the agent, and reports what it did here.
    /// <para>
    /// This path is deliberately the narrow one. It is reached only when somebody asked for the
    /// agent by name, and the only difference it makes is which engine answers — the message still
    /// goes into the same conversation, and the answer still appears in the same transcript.
    /// </para>
    /// <para>
    /// Approvals are not answered from here. A prompt that asks somebody to agree to writing a
    /// file has to be an explicit, labelled, deliberate act, and a yes typed into a chat window
    /// is not that: it is a word in a transcript that may have been sent to a provider and
    /// scrolled past. So a run that needs approval is reported as needing one, and the person
    /// goes to the workspace, where the prompt names the action and has two buttons.
    /// </para>
    /// </summary>
    private async Task RunAgentAsync(AgentRequestContext context, CancellationToken cancellationToken)
    {
        EventHandler<AgentProgress>? report = (_, progress) =>
            OnAgentProgress(progress);

        _agent.ProgressChanged += report;

        try
        {
            // Recorded by hand because the message never reaches a provider on this path, and a
            // conversation that showed a request it could not replay would be a different
            // conversation from the one the person remembers.
            await _conversations
                .AddMessageAsync(
                    ConversationId,
                    AIMessage.CreateUser(context.Request),
                    cancellationToken)
                .ConfigureAwait(true);

            var result = await _agent.RunAsync(context, cancellationToken).ConfigureAwait(true);

            if (result.IsFailure || result.Value is null)
            {
                Append(ChatMessageViewModel.FromStatus(DescribeFailure(result)));
                return;
            }

            var finished = result.Value;

            if (finished.WasCancelled)
            {
                Append(ChatMessageViewModel.FromStatus("Stopped."));
                return;
            }

            Append(ChatMessageViewModel.FromAssistant(finished.FinalResponse));

            await _conversations
                .AddMessageAsync(
                    ConversationId,
                    AIMessage.CreateAssistant(finished.FinalResponse),
                    cancellationToken)
                .ConfigureAwait(true);
        }
        finally
        {
            _agent.ProgressChanged -= report;
        }
    }

    /// <summary>
    /// Reports a step boundary as a status line, so a run that takes a few seconds says what it
    /// is doing rather than appearing to hang.
    /// </summary>
    private void OnAgentProgress(AgentProgress progress)
    {
        switch (progress.Kind)
        {
            case AgentProgressKind.PlanReady when progress.Plan is { } plan:
                Append(ChatMessageViewModel.FromStatus(
                    $"Plan: {progress.TotalSteps} step(s) — {string.Join(", ", plan.PlannedTools)}"));
                break;

            case AgentProgressKind.StepStarted:
                Append(ChatMessageViewModel.FromStatus(
                    $"Step {progress.StepOrder} of {progress.TotalSteps}: {progress.ToolName}"));
                break;

            case AgentProgressKind.AwaitingApproval:
                // Named rather than silently waiting. A run sitting on an approval nobody can see
                // would look exactly like a run that had hung.
                Append(ChatMessageViewModel.FromStatus(
                    $"Waiting for your approval: {progress.Approval!.Action}. " +
                    "Open the Agent workspace to approve or refuse it."));
                break;

            case AgentProgressKind.Completed:
                if (progress.Message is not null)
                {
                    Append(ChatMessageViewModel.FromStatus(progress.Message));
                }

                break;
        }
    }

    /// <summary>
    /// Turns a run that did not succeed into a sentence for the transcript.
    /// <para>
    /// The code names the situation and the tools named, and nothing else. A run's own error text
    /// is written by a tool or a provider, which is a place their words could come back out, so
    /// it is not quoted here.
    /// </para>
    /// </summary>
    private static string DescribeFailure(Result<AgentExecutionResult> result)
    {
        if (result.Value?.WasRejected == true)
        {
            return "The agent stopped because that step was refused. Nothing was changed.";
        }

        if (result.ErrorCode is { } code)
        {
            return $"The agent could not finish that ({code}). Nothing was changed.";
        }

        return "The agent could not finish that. Nothing was changed.";
    }

    /// <summary>
    /// Stops the answer in flight. The text already written stays, which is the point: a person
    /// who stops a long answer has usually read enough of it to be useful.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => _generation?.Cancel();

    /// <summary>
    /// Cancels anything in flight and empties the transcript. A new chat that left a request
    /// running would keep writing into a conversation nobody is looking at.
    /// </summary>
    [RelayCommand]
    private async Task NewChatAsync()
    {
        _generation?.Cancel();
        await _ready.ConfigureAwait(true);

        Messages.Clear();
        OnPropertyChanged(nameof(IsEmpty));
        FinishStreaming();

        await _conversations
            .ClearConversationAsync(ConversationId, CancellationToken.None)
            .ConfigureAwait(true);

        Append(ChatMessageViewModel.FromStatus("New conversation."));
    }

    /// <summary>
    /// Runs the streamed path, growing one assistant message as the answer arrives.
    /// </summary>
    private async Task StreamAsync(string text, CancellationToken cancellationToken)
    {
        _streamingMessage = ChatMessageViewModel.FromAssistant(string.Empty);
        Append(_streamingMessage);
        _streamingText.Clear();
        CurrentStreamingText = string.Empty;

        var command = new SendMessageCommand(text, ConversationId);

        // The awaits deliberately do not leave the interface thread: the text is bound to the
        // message and the status line, and updating those from another thread would throw.
        await foreach (var update in _streamMessages.HandleAsync(command, cancellationToken))
        {
            switch (update.Kind)
            {
                case AIStreamUpdateKind.Delta:
                    _streamingText.Append(update.Text);
                    Publish();
                    break;

                case AIStreamUpdateKind.Completed:
                    _streamingText.Clear().Append(update.Text);
                    Publish();
                    FinishStreaming();
                    break;

                case AIStreamUpdateKind.Failed:
                    Publish();
                    FinishStreaming();
                    Append(ChatMessageViewModel.FromStatus(
                        update.ErrorMessage ?? "The answer stopped before it finished."));
                    break;
            }
        }
    }

    /// <summary>
    /// Runs the whole-answer path, for when streaming is turned off in Settings.
    /// </summary>
    private async Task SendWholeAnswerAsync(string text, CancellationToken cancellationToken)
    {
        var response = await _sendMessages
            .HandleAsync(new SendMessageCommand(text, ConversationId), cancellationToken);

        if (!response.IsSuccessful)
        {
            Append(ChatMessageViewModel.FromStatus(
                response.ErrorMessage ?? "The assistant could not answer that."));
            return;
        }

        Append(ChatMessageViewModel.FromAssistant(response.Content));
    }

    /// <summary>
    /// Pushes the accumulated text onto the message and the status line.
    /// </summary>
    private void Publish()
    {
        var text = _streamingText.ToString();
        CurrentStreamingText = text;

        if (_streamingMessage is not null)
        {
            _streamingMessage.Update(text);
        }
    }

    /// <summary>
    /// Stops tracking the answer in progress, leaving whatever was written on the message.
    /// </summary>
    private void FinishStreaming()
    {
        _streamingMessage = null;
        CurrentStreamingText = string.Empty;
    }

    private void Append(ChatMessageViewModel message)
    {
        Messages.Add(message);
        OnPropertyChanged(nameof(IsEmpty));
    }

    private async Task InitialiseConversationAsync()
    {
        try
        {
            var created = await _conversations.CreateConversationAsync(CancellationToken.None);
            ConversationId = created.Id;
        }
        catch (Exception exception)
        {
            // Started before anyone can await it, so a failure here would never be observed
            // by the constructor and would leave _ready permanently faulted: every later
            // command would rethrow and the page would be unusable with no way back. The
            // person is told in the transcript instead, and the exception is left to the
            // debugger rather than written anywhere, since it is not about their question.
            System.Diagnostics.Debug.WriteLine($"Chat conversation could not be created: {exception}");
            Append(ChatMessageViewModel.FromStatus("A new conversation could not be started."));
        }
    }

    private CancellationTokenSource BeginGeneration()
    {
        // A request already in flight is cancelled rather than joined: two answers appending to
        // one transcript would interleave into something neither person nor assistant wrote.
        _generation?.Cancel();
        _generation?.Dispose();

        var generation = new CancellationTokenSource();
        _generation = generation;
        IsGenerating = true;
        return generation;
    }

    private void EndGeneration(CancellationTokenSource generation)
    {
        IsGenerating = false;

        if (ReferenceEquals(_generation, generation))
        {
            _generation = null;
        }

        generation.Dispose();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _generation?.Cancel();
        _generation?.Dispose();
        _generation = null;

        base.Dispose();
    }
}
