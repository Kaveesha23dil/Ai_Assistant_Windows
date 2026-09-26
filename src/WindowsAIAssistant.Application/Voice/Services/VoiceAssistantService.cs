using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Application.Common.Errors;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Voice.Services;

/// <summary>
/// Orchestrates the voice pipeline: recognition, intent recognition, routing, and the spoken
/// reply, while owning the lifecycle state the user interface binds to.
/// <para>
/// The service is deliberately the only component that advances the state machine. Handlers
/// return a result and the service decides what the user hears next, which is what keeps
/// states such as Processing and Executing from leaking into the handlers and turning into
/// scattered boolean flags.
/// </para>
/// </summary>
public sealed class VoiceAssistantService : IVoiceAssistantService, IDisposable
{
    private const string BusyMessage = "The assistant is already working on something.";
    private const string NotRecognizedMessage = "I didn't understand that command.";
    private const string NothingToConfirmMessage = "There is nothing waiting for confirmation.";
    private static readonly TimeSpan BusyTimeout = TimeSpan.FromSeconds(5);

    private readonly ISpeechRecognitionService _recognition;
    private readonly ISpeechSynthesisService _synthesis;
    private readonly IIntentRecognizer _intentRecognizer;
    private readonly ICommandRouter _router;
    private readonly IVoiceAssistantControl _control;
    private readonly IVoiceCommandHistory _history;
    private readonly IPermissionService _permissions;
    private readonly IErrorHandler _errorHandler;
    private readonly AssistantSession _session;
    private readonly VoiceIntentPolicy _policy;
    private readonly ILogger<VoiceAssistantService> _logger;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private bool _disposed;

    public VoiceAssistantService(
        ISpeechRecognitionService recognition,
        ISpeechSynthesisService synthesis,
        IIntentRecognizer intentRecognizer,
        ICommandRouter router,
        IVoiceAssistantControl control,
        IVoiceCommandHistory history,
        IPermissionService permissions,
        IErrorHandler errorHandler,
        AssistantSession session,
        VoiceIntentPolicy policy,
        ILogger<VoiceAssistantService> logger)
    {
        ArgumentNullException.ThrowIfNull(recognition);
        ArgumentNullException.ThrowIfNull(synthesis);
        ArgumentNullException.ThrowIfNull(intentRecognizer);
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(errorHandler);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(logger);

        _recognition = recognition;
        _synthesis = synthesis;
        _intentRecognizer = intentRecognizer;
        _router = router;
        _control = control;
        _history = history;
        _permissions = permissions;
        _errorHandler = errorHandler;
        _session = session;
        _policy = policy;
        _logger = logger;

        _session.StateChanged += OnSessionStateChanged;
        _recognition.PartialTranscriptReceived += OnPartialTranscript;
        _recognition.FinalTranscriptReceived += OnFinalTranscript;
        _recognition.ListeningStarted += OnListeningStarted;
        _recognition.ListeningStopped += OnListeningStopped;
        _recognition.RecognitionFailed += OnRecognitionFailed;
    }

    /// <inheritdoc />
    public event EventHandler<VoiceStateChangedEventArgs>? StateChanged;

    /// <inheritdoc />
    public event EventHandler<VoiceTranscriptEventArgs>? TranscriptUpdated;

    /// <inheritdoc />
    public event EventHandler<VoiceResponseEventArgs>? ResponseProduced;

    /// <inheritdoc />
    public VoiceAssistantState State => _session.State;

    /// <inheritdoc />
    public bool IsEnabled =>
        _policy.Enabled && _permissions.IsGranted(PermissionCapability.Microphone);

    /// <inheritdoc />
    public string? LastResponse => _session.LastResponse;

    /// <inheritdoc />
    public async Task<Result> StartListeningAsync(CancellationToken cancellationToken = default)
    {
        if (!_policy.Enabled)
        {
            return Result.Failure("The voice assistant is currently switched off.");
        }

        if (_recognition.IsListening)
        {
            return Result.Success();
        }

        return await _control.StartListeningAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> StopListeningAsync(CancellationToken cancellationToken = default)
    {
        if (!_recognition.IsListening)
        {
            return Result.Success();
        }

        _logger.LogInformation("Voice listening stopped by the user.");
        return await _control.StopListeningAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result> CancelAsync(CancellationToken cancellationToken = default) =>
        _control.CancelAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Result> StopSpeakingAsync(CancellationToken cancellationToken = default) =>
        _control.StopSpeakingAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<Result> RepeatAsync(CancellationToken cancellationToken = default)
    {
        var last = _session.LastResponse;
        if (string.IsNullOrWhiteSpace(last))
        {
            return Result.Failure("There is nothing to repeat yet.");
        }

        _logger.LogInformation("Repeating the previous assistant response.");
        return await _control.SpeakAsync(last, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result> SpeakAsync(string text, CancellationToken cancellationToken = default) =>
        _control.SpeakAsync(text, cancellationToken);

    /// <inheritdoc />
    public async Task<VoiceCommandResult> ProcessTranscriptAsync(
        string transcript,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transcript);

        if (!await _operationGate.WaitAsync(BusyTimeout, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogInformation("Voice request rejected because another operation is in progress.");
            return VoiceCommandResult.Failure(
                VoiceCommand.Create(transcript, AssistantIntent.Unknown),
                ErrorCodes.VoiceBusy,
                BusyMessage);
        }

        try
        {
            if (!_session.TryBeginOperation(out var scope) || scope is null)
            {
                return VoiceCommandResult.Failure(
                    VoiceCommand.Create(transcript, AssistantIntent.Unknown),
                    ErrorCodes.VoiceBusy,
                    BusyMessage);
            }

            using (scope)
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _session.ActiveOperationToken))
            {
                return await ProcessCoreAsync(transcript, 1.0, linked.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<VoiceCommandResult> ProcessCoreAsync(
        string transcript,
        double recognitionConfidence,
        CancellationToken cancellationToken)
    {
        _session.TransitionTo(VoiceAssistantState.Processing);

        var recognized = await _intentRecognizer
            .RecognizeAsync(transcript, recognitionConfidence, cancellationToken)
            .ConfigureAwait(false);

        if (recognized.IsFailure || recognized.Value is null)
        {
            _logger.LogInformation("Voice transcript was not recognized.");
            return await PublishAsync(
                VoiceCommandResult.Failure(
                    VoiceCommand.Create(transcript, AssistantIntent.Unknown),
                    ErrorCodes.VoiceCommandNotRecognized,
                    NotRecognizedMessage),
                cancellationToken).ConfigureAwait(false);
        }

        var command = ApplyConfidencePolicy(recognized.Value);

        // Confirmation is answered by the assistant itself rather than by a handler, because
        // re-routing the held-back command has to happen before the router is consulted again.
        return command.Intent == AssistantIntent.ConfirmCommand
            ? await ConfirmPendingAsync(command, cancellationToken).ConfigureAwait(false)
            : await ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Demands confirmation for a weakly recognized command so a fuzzy match is never executed
    /// on a guess.
    /// </summary>
    private VoiceCommand ApplyConfidencePolicy(VoiceCommand command)
    {
        if (!_policy.ConfirmLowConfidenceCommands
            || command.Confidence >= _policy.MinimumCommandConfidence
            || command.SafetyLevel != ActionSafetyLevel.Safe)
        {
            return command;
        }

        _logger.LogInformation(
            "Voice action {Intent} held for confirmation because confidence {Confidence:0.00} is below the threshold.",
            command.Intent,
            command.Confidence);

        return command with { RequiresConfirmation = true };
    }

    private async Task<VoiceCommandResult> ExecuteAsync(
        VoiceCommand command,
        CancellationToken cancellationToken)
    {
        _session.TransitionTo(VoiceAssistantState.Executing);

        VoiceCommandResult result;
        try
        {
            result = await _router.RouteAsync(command, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _session.TransitionTo(VoiceAssistantState.Idle);
            throw;
        }
        catch (Exception exception)
        {
            var error = _errorHandler.Handle(exception, "VoiceCommandRouting");
            _session.TransitionTo(VoiceAssistantState.Error);
            result = VoiceCommandResult.Failure(command, error.Code, error.Message);
        }

        if (result.RequiresConfirmation && result.IsFailure)
        {
            _session.SetPendingCommand(command);
        }
        else
        {
            _session.ClearPendingCommand();
        }

        _session.TransitionTo(VoiceAssistantState.Idle);
        return await PublishAsync(result, cancellationToken).ConfigureAwait(false);
    }

    private async Task<VoiceCommandResult> ConfirmPendingAsync(
        VoiceCommand confirmation,
        CancellationToken cancellationToken)
    {
        var pending = _session.PendingCommand;
        if (pending is null)
        {
            return await PublishAsync(
                VoiceCommandResult.Failure(confirmation, ErrorCodes.ValidationError, NothingToConfirmMessage),
                cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Voice action {Intent} confirmed by the user.", pending.Intent);
        _session.ClearPendingCommand();

        return await ExecuteAsync(pending with { IsConfirmed = true }, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<VoiceCommandResult> PublishAsync(
        VoiceCommandResult result,
        CancellationToken cancellationToken)
    {
        _history.Record(result);
        _session.RememberResponse(result.ResponseText);

        var wasSpoken = false;
        if (!_policy.SpeakResponses || string.IsNullOrWhiteSpace(result.ResponseText))
        {
            wasSpoken = false;
        }
        else if (cancellationToken.IsCancellationRequested)
        {
            // The operation was cancelled while the handler ran, which is exactly what the
            // "cancel" command asks for. Speaking now would talk over the user who just
            // interrupted, so the response is still raised for the interface but not read out.
            _logger.LogInformation("Response was not spoken because the operation was cancelled.");
            wasSpoken = false;
        }
        else
        {
            var spoken = await _control.SpeakAsync(result.ResponseText, cancellationToken).ConfigureAwait(false);
            wasSpoken = spoken.IsSuccess;
        }

        ResponseProduced?.Invoke(this, new VoiceResponseEventArgs(result, wasSpoken));
        return result;
    }

    private void OnSessionStateChanged(object? sender, VoiceStateChangedEventArgs args) =>
        StateChanged?.Invoke(this, args);

    private void OnPartialTranscript(object? sender, VoiceTranscriptEventArgs args) =>
        TranscriptUpdated?.Invoke(this, args);

    private void OnFinalTranscript(object? sender, VoiceTranscriptEventArgs args)
    {
        TranscriptUpdated?.Invoke(this, args);

        if (!args.Result.HasText)
        {
            _session.TransitionTo(VoiceAssistantState.Idle);
            return;
        }

        // A recognizer callback cannot be awaited, so the command runs on the thread pool.
        // Failures inside the pipeline are already converted into user-safe results.
        _ = Task.Run(async () =>
        {
            await ProcessCoreAsync(args.Result.Text, args.Result.Confidence, CancellationToken.None)
                .ConfigureAwait(false);

            await ResumeContinuousListeningAsync().ConfigureAwait(false);
        });
    }

    /// <summary>
    /// Restarts listening when continuous mode is switched on. It stays off by default, so
    /// the microphone is only ever open at the user's request.
    /// </summary>
    private async Task ResumeContinuousListeningAsync()
    {
        if (!_policy.EnableContinuousListening || !IsEnabled || _session.State != VoiceAssistantState.Idle)
        {
            return;
        }

        var resumed = await StartListeningAsync().ConfigureAwait(false);
        if (resumed.IsFailure)
        {
            _logger.LogInformation("Continuous listening did not resume.");
        }
    }

    private void OnListeningStarted(object? sender, VoiceStateChangedEventArgs args) =>
        _session.TransitionTo(VoiceAssistantState.Listening);

    private void OnListeningStopped(object? sender, VoiceStateChangedEventArgs args)
    {
        if (_session.State is VoiceAssistantState.Listening)
        {
            _session.TransitionTo(VoiceAssistantState.Idle);
        }
    }

    private void OnRecognitionFailed(object? sender, VoiceRecognitionFailedEventArgs args)
    {
        _logger.LogWarning("Voice recognition failed with code {ErrorCode}.", args.ErrorCode);
        _session.TransitionTo(VoiceAssistantState.Error);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _session.StateChanged -= OnSessionStateChanged;
        _recognition.PartialTranscriptReceived -= OnPartialTranscript;
        _recognition.FinalTranscriptReceived -= OnFinalTranscript;
        _recognition.ListeningStarted -= OnListeningStarted;
        _recognition.ListeningStopped -= OnListeningStopped;
        _recognition.RecognitionFailed -= OnRecognitionFailed;

        _operationGate.Dispose();
    }
}
