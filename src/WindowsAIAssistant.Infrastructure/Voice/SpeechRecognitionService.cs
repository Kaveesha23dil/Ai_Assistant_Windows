using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Windows.Foundation;
using Windows.Globalization;
using Windows.Media.SpeechRecognition;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.Voice;

/// <summary>
/// Recognizes speech with the on-device Windows recognizer.
/// <para>
/// A session is one call to <c>RecognizeAsync</c>, awaited to completion. That shape fits a
/// desktop assistant better than the event-driven recognizer session the same API offers for
/// shell applications: a command is one bounded utterance, the microphone closes as soon as
/// the result settles, and there is no continuous capture unless the user asked for it.
/// </para>
/// <para>
/// Audio never leaves the machine. The local recognizer is the only engine used, so no
/// microphone audio is sent anywhere and the cloud-speech consent switch has nothing to
/// switch on here. Nothing raised by this class contains a transcript in a log call.
/// </para>
/// </summary>
public sealed class SpeechRecognitionService : ISpeechRecognitionService, IDisposable
{
    private const int StartSignalTimeoutMilliseconds = 5000;
    private const int StopSignalTimeoutMilliseconds = 3000;
    private const int MinimumSilenceTimeoutMilliseconds = 200;
    private const int MaximumSilenceTimeoutMilliseconds = 30000;

    // Named HRESULTs, so the mapping reads as an intent rather than a wall of hex.
    private const int AccessDenied = unchecked((int)0x80070005);
    private const int NotSupported = unchecked((int)0x80070032);
    private const int OutOfMemory = unchecked((int)0x8007000E);
    private const int HostNotReachable = unchecked((int)0x80072EE7);
    private const int Timeout = unchecked((int)0x80072EE2);

    /// <summary>
    /// How long the microphone waits for the user to begin speaking. The platform default is
    /// roughly this long; it is set explicitly so a slow start does not silently end the
    /// session with "no match" before the user has said anything.
    /// </summary>
    private const int InitialSilenceTimeoutMilliseconds = 8000;

    private readonly ILogger<SpeechRecognitionService> _logger;
    private readonly IOptionsMonitor<VoiceOptions> _voiceOptions;
    private readonly IOptionsMonitor<PrivacyOptions> _privacyOptions;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private SpeechRecognizer? _recognizer;
    private TaskCompletionSource<bool>? _listeningSignalled;
    private bool _discardTranscript;
    private string _language = "en-US";
    private bool _disposed;

    public SpeechRecognitionService(
        IOptionsMonitor<VoiceOptions> voiceOptions,
        IOptionsMonitor<PrivacyOptions> privacyOptions,
        ILogger<SpeechRecognitionService> logger)
    {
        ArgumentNullException.ThrowIfNull(voiceOptions);
        ArgumentNullException.ThrowIfNull(privacyOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _voiceOptions = voiceOptions;
        _privacyOptions = privacyOptions;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsListening => Volatile.Read(ref _recognizer) is not null;

    /// <inheritdoc />
    public bool IsAvailable
    {
        get
        {
            if (_disposed)
            {
                return false;
            }

            // The consent check is repeated here even though the permission service already
            // gates every command. This is the one class that actually opens the microphone,
            // so the last check before capture belongs to it.
            if (!_privacyOptions.CurrentValue.AllowVoiceProcessing ||
                !_privacyOptions.CurrentValue.AllowMicrophoneAccess)
            {
                return false;
            }

            try
            {
                _ = SpeechRecognizer.SystemSpeechLanguage;
                return true;
            }
            catch (Exception exception)
            {
                _logger.LogDebug(
                    "The local speech recognizer is unavailable (HRESULT 0x{Result:X8}).",
                    exception.HResult);
                return false;
            }
        }
    }

    /// <inheritdoc />
    public string Language => _language;

    /// <inheritdoc />
    public event EventHandler<VoiceStateChangedEventArgs>? ListeningStarted;

    /// <inheritdoc />
    public event EventHandler<VoiceStateChangedEventArgs>? ListeningStopped;

    /// <inheritdoc />
    public event EventHandler<VoiceTranscriptEventArgs>? PartialTranscriptReceived;

    /// <inheritdoc />
    public event EventHandler<VoiceTranscriptEventArgs>? FinalTranscriptReceived;

    /// <inheritdoc />
    public event EventHandler<VoiceRecognitionFailedEventArgs>? RecognitionFailed;

    /// <inheritdoc />
    public async Task<Result> StartListeningAsync(
        SpeechRecognitionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        // A start that cannot possibly work is reported through the returned result rather
        // than the event, because the caller is right here and can act on it immediately.
        if (!_voiceOptions.CurrentValue.Enabled)
        {
            return Result.Failure("The voice assistant is switched off in Settings.");
        }

        var privacy = _privacyOptions.CurrentValue;
        if (!privacy.AllowVoiceProcessing || !privacy.AllowMicrophoneAccess)
        {
            return Result.Failure("Microphone access is switched off in Privacy settings.");
        }

        var effective = options ?? FromConfiguration(_voiceOptions.CurrentValue);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsListening)
            {
                return Result.Success();
            }

            var languageTag = ResolveLanguage(effective.Language);
            _language = languageTag;

            SpeechRecognizer recognizer;
            try
            {
                recognizer = new SpeechRecognizer(new Language(languageTag));
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    "The speech recognizer could not be created (HRESULT 0x{Result:X8}).",
                    exception.HResult);

                RaiseRecognitionFailed(
                    ErrorCodes.VoiceMicrophoneUnavailable,
                    "Speech recognition is unavailable on this device.",
                    isRecoverable: false);
                return Result.Failure("Speech recognition is unavailable on this device.");
            }

            ApplyTimeouts(recognizer, effective.SilenceTimeoutMilliseconds);

            _discardTranscript = false;
            _listeningSignalled = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            recognizer.StateChanged += OnStateChanged;
            if (effective.ReportPartialResults)
            {
                recognizer.HypothesisGenerated += OnHypothesisGenerated;
            }

            _recognizer = recognizer;

            IAsyncOperation<SpeechRecognitionResult> operation;
            try
            {
                operation = recognizer.RecognizeAsync();
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    "The microphone could not be opened (HRESULT 0x{Result:X8}).",
                    exception.HResult);

                ReleaseRecognizer(recognizer);
                RaiseRecognitionFailed(
                    MapToErrorCode(exception.HResult),
                    DescribeFailure(exception.HResult),
                    isRecoverable: true);
                return Result.Failure(DescribeFailure(exception.HResult));
            }

            // Observed but not awaited: the session finishes on its own and reports through
            // the transcript and failure events.
            _ = RunSessionAsync(operation);

            var started = await WaitForListeningAsync(cancellationToken).ConfigureAwait(false);
            if (!started)
            {
                await StopRecognizerAsync(recognizer).ConfigureAwait(false);
                return Result.Failure("The microphone did not start listening in time.");
            }

            ListeningStarted?.Invoke(
                this,
                new VoiceStateChangedEventArgs(VoiceAssistantState.Idle, VoiceAssistantState.Listening));

            return Result.Success();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Result> StopListeningAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var recognizer = Volatile.Read(ref _recognizer);
            if (recognizer is null)
            {
                return Result.Success();
            }

            // Whatever was heard so far has already been reported as a partial result, so a
            // stop keeps the session and simply lets it settle.
            _discardTranscript = false;

            await StopRecognizerAsync(recognizer).ConfigureAwait(false);
            return Result.Success();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Result> CancelListeningAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var recognizer = Volatile.Read(ref _recognizer);
            if (recognizer is null)
            {
                return Result.Success();
            }

            // The single-shot recognizer has no cancel of its own, so the session is ended the
            // same way a stop ends it and the transcript is dropped on the way out.
            _discardTranscript = true;

            await StopRecognizerAsync(recognizer).ConfigureAwait(false);
            return Result.Success();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        var recognizer = Interlocked.Exchange(ref _recognizer, null);
        if (recognizer is not null)
        {
            try
            {
                recognizer.StopRecognitionAsync().AsTask().Wait(StopSignalTimeoutMilliseconds);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(
                    "Stopping the recognizer during disposal failed (HRESULT 0x{Result:X8}).",
                    exception.HResult);
            }

            ReleaseRecognizer(recognizer);
        }

        _gate.Dispose();
    }

    private static SpeechRecognitionOptions FromConfiguration(VoiceOptions options) => new()
    {
        Language = options.Language,
        SilenceTimeoutMilliseconds = options.SilenceTimeoutMilliseconds,
        ReportPartialResults = options.ReportPartialResults
    };

    private static string ResolveLanguage(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return SpeechRecognizer.SystemSpeechLanguage.LanguageTag;
        }

        var tag = requested.Trim();
        try
        {
            return new Language(tag).LanguageTag;
        }
        catch (ArgumentException)
        {
            // An unusable tag falls back to whatever the machine is set up for rather than
            // failing the whole command over a configuration typo.
            return SpeechRecognizer.SystemSpeechLanguage.LanguageTag;
        }
    }

    private static void ApplyTimeouts(SpeechRecognizer recognizer, int silenceTimeoutMilliseconds)
    {
        var endSilence = Math.Clamp(
            silenceTimeoutMilliseconds,
            MinimumSilenceTimeoutMilliseconds,
            MaximumSilenceTimeoutMilliseconds);

        recognizer.Timeouts.EndSilenceTimeout = TimeSpan.FromMilliseconds(endSilence);
        recognizer.Timeouts.InitialSilenceTimeout =
            TimeSpan.FromMilliseconds(InitialSilenceTimeoutMilliseconds);
    }

    private async Task<bool> WaitForListeningAsync(CancellationToken cancellationToken)
    {
        var signal = _listeningSignalled;
        if (signal is null)
        {
            return false;
        }

        try
        {
            return await signal.Task
                .WaitAsync(TimeSpan.FromMilliseconds(StartSignalTimeoutMilliseconds), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private async Task RunSessionAsync(
        IAsyncOperation<SpeechRecognitionResult> operation)
    {
        try
        {
            var result = await operation.AsTask().ConfigureAwait(false);

            // A cancel drops the utterance, including a result that happened to settle first.
            if (!_discardTranscript)
            {
                PublishResult(result);
            }
        }
        catch (OperationCanceledException)
        {
            // The caller cancelled; the partials already reported are the whole story.
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Speech recognition failed (HRESULT 0x{Result:X8}).",
                exception.HResult);

            _listeningSignalled?.TrySetResult(false);
            RaiseRecognitionFailed(
                MapToErrorCode(exception.HResult),
                DescribeFailure(exception.HResult),
                isRecoverable: true);
        }
        finally
        {
            _listeningSignalled?.TrySetResult(false);

            var recognizer = Interlocked.Exchange(ref _recognizer, null);
            if (recognizer is not null)
            {
                ReleaseRecognizer(recognizer);
            }

            ListeningStopped?.Invoke(
                this,
                new VoiceStateChangedEventArgs(
                    VoiceAssistantState.Listening,
                    VoiceAssistantState.Idle));
        }
    }

    private void PublishResult(SpeechRecognitionResult result)
    {
        switch (result.Status)
        {
            case SpeechRecognitionResultStatus.Success:
                if (string.IsNullOrWhiteSpace(result.Text))
                {
                    RaiseRecognitionFailed(
                        ErrorCodes.VoiceCommandNotRecognized,
                        "I didn't catch that.",
                        isRecoverable: true);
                    return;
                }

                FinalTranscriptReceived?.Invoke(
                    this,
                    new VoiceTranscriptEventArgs(
                        new VoiceRecognitionResult(
                            result.Text,
                            isFinal: true,
                            MapConfidence(result),
                            DateTimeOffset.Now)));
                return;

            case SpeechRecognitionResultStatus.UserCanceled:
                // The session was ended on purpose, so the transcripts already reported stand.
                return;

            case SpeechRecognitionResultStatus.AudioQualityFailure:
                RaiseRecognitionFailed(
                    ErrorCodes.VoiceMicrophoneUnavailable,
                    "I couldn't hear that clearly.",
                    isRecoverable: true);
                return;

            case SpeechRecognitionResultStatus.TopicLanguageNotSupported:
            case SpeechRecognitionResultStatus.GrammarLanguageMismatch:
            case SpeechRecognitionResultStatus.GrammarCompilationFailure:
                RaiseRecognitionFailed(
                    ErrorCodes.VoiceActionNotAvailable,
                    $"Speech recognition isn't available for {Language} on this device.",
                    isRecoverable: false);
                return;

            default:
                RaiseRecognitionFailed(
                    ErrorCodes.VoiceRecognitionFailed,
                    "Speech recognition failed.",
                    isRecoverable: true);
                return;
        }
    }

    private static double MapConfidence(SpeechRecognitionResult result)
    {
        // The raw score is a genuine 0 to 1 confidence. The coarse enum is only a fallback for
        // the rare case where the recognizer withholds the number.
        if (result.RawConfidence > 0.0)
        {
            return Math.Clamp(result.RawConfidence, 0.0, 1.0);
        }

        return result.Confidence switch
        {
            SpeechRecognitionConfidence.High => 1.0,
            SpeechRecognitionConfidence.Medium => 0.6,
            SpeechRecognitionConfidence.Low => 0.3,
            SpeechRecognitionConfidence.Rejected => 0.0,
            _ => 0.5
        };
    }

    private static string MapToErrorCode(int hresult) => hresult switch
    {
        AccessDenied => ErrorCodes.VoicePermissionDenied,
        NotSupported => ErrorCodes.VoiceActionNotAvailable,
        OutOfMemory => ErrorCodes.VoiceMicrophoneUnavailable,
        _ => ErrorCodes.VoiceRecognitionFailed
    };

    private static string DescribeFailure(int hresult) => hresult switch
    {
        AccessDenied => "Windows denied access to the microphone.",
        NotSupported => "Speech recognition isn't supported on this device.",
        OutOfMemory => "There wasn't enough memory to start the microphone.",
        HostNotReachable => "Speech recognition couldn't be reached. Check your connection and try again.",
        Timeout => "Speech recognition timed out. Try again.",
        _ => "Speech recognition failed."
    };

    private async Task StopRecognizerAsync(SpeechRecognizer recognizer)
    {
        try
        {
            await recognizer.StopRecognitionAsync()
                .AsTask()
                .WaitAsync(TimeSpan.FromMilliseconds(StopSignalTimeoutMilliseconds))
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Ending the session fails for harmless reasons, such as it having already ended
            // on its own. The session task still completes and releases the recognizer.
            _logger.LogDebug(
                "Stopping the recognizer reported HRESULT 0x{Result:X8}.",
                exception.HResult);
        }
    }

    private void OnStateChanged(SpeechRecognizer sender, SpeechRecognizerStateChangedEventArgs args)
    {
        if (args.State is SpeechRecognizerState.Capturing or SpeechRecognizerState.SoundStarted)
        {
            _listeningSignalled?.TrySetResult(true);
        }
    }

    private void OnHypothesisGenerated(
        SpeechRecognizer sender,
        SpeechRecognitionHypothesisGeneratedEventArgs args)
    {
        var text = args.Hypothesis?.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        // An interim hypothesis carries no score. The neutral midpoint is reported so that
        // nothing downstream can mistake a guess for a settled result.
        PartialTranscriptReceived?.Invoke(
            this,
            new VoiceTranscriptEventArgs(
                new VoiceRecognitionResult(
                    text,
                    isFinal: false,
                    confidence: 0.5,
                    DateTimeOffset.Now)));
    }

    private void ReleaseRecognizer(SpeechRecognizer recognizer)
    {
        recognizer.StateChanged -= OnStateChanged;
        recognizer.HypothesisGenerated -= OnHypothesisGenerated;

        try
        {
            recognizer.Dispose();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                "Disposing the recognizer reported HRESULT 0x{Result:X8}.",
                exception.HResult);
        }
    }

    private void RaiseRecognitionFailed(string errorCode, string message, bool isRecoverable) =>
        RecognitionFailed?.Invoke(this, new VoiceRecognitionFailedEventArgs(errorCode, message, isRecoverable));
}
