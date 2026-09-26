using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Abstractions.Time;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Tests.Fakes;

/// <summary>
/// Stands in for the Windows recognizer so a test can drive transcripts deterministically.
/// <para>
/// The real implementation owns a microphone and a native recognizer, so neither can be
/// exercised from a unit test. This fake exposes the same events and lets a test supply the
/// text, which is what the pipeline actually consumes.
/// </para>
/// </summary>
public sealed class FakeSpeechRecognitionService : ISpeechRecognitionService
{
    private readonly List<string> _partials = [];

    public string NextTranscript { get; set; } = "what is the battery level";

    public string Language { get; set; } = "en-US";

    public bool IsAvailable { get; set; } = true;

    public bool IsListening { get; private set; }

    /// <summary>Gets the number of times a session was started.</summary>
    public int StartCallCount { get; private set; }

    /// <summary>Gets or sets the result returned by the next start.</summary>
    public Result StartResult { get; set; } = Result.Success();

    public Result StopResult { get; set; } = Result.Success();

    public Result CancelResult { get; set; } = Result.Success();

    /// <summary>
    /// Gets or sets a failure to raise instead of a transcript, so a test can prove the
    /// interface surfaces a denied microphone rather than staying silent.
    /// </summary>
    public string? FailWith { get; set; }

    /// <summary>Gets the code of the last raised recognition failure.</summary>
    public string? LastFailureCode { get; private set; }

    /// <summary>Gets the last options a caller passed to a start.</summary>
    public SpeechRecognitionOptions? LastOptions { get; private set; }

    public IReadOnlyList<string> RaisedPartials => _partials;

    public event EventHandler<VoiceStateChangedEventArgs>? ListeningStarted;

    public event EventHandler<VoiceStateChangedEventArgs>? ListeningStopped;

    public event EventHandler<VoiceTranscriptEventArgs>? PartialTranscriptReceived;

    public event EventHandler<VoiceTranscriptEventArgs>? FinalTranscriptReceived;

    public event EventHandler<VoiceRecognitionFailedEventArgs>? RecognitionFailed;

    public Task<Result> StartListeningAsync(
        SpeechRecognitionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastOptions = options;
        StartCallCount++;

        if (StartResult.IsFailure)
        {
            Fail("StartFailed", StartResult.ErrorMessage ?? "Recognition could not start.");
            return Task.FromResult(StartResult);
        }

        if (!IsListening)
        {
            IsListening = true;
            ListeningStarted?.Invoke(this, new VoiceStateChangedEventArgs(
                VoiceAssistantState.Idle,
                VoiceAssistantState.Listening));
        }

        return Task.FromResult(Result.Success());
    }

    public Task<Result> StopListeningAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (StopResult.IsFailure)
        {
            Fail("StopFailed", StopResult.ErrorMessage ?? "Recognition could not stop.");
            return Task.FromResult(StopResult);
        }

        IsListening = false;

        if (FailWith is not null)
        {
            Fail("NoSpeechRecognized", FailWith);
            ListeningStopped?.Invoke(this, new VoiceStateChangedEventArgs(
                VoiceAssistantState.Listening,
                VoiceAssistantState.Idle));
            return Task.FromResult(Result.Success());
        }

        // A partial first, so a test can prove the view model overwrites it with the final text.
        Raise(NextTranscript, isFinal: false);
        var final = Raise(NextTranscript, isFinal: true);

        FinalTranscriptReceived?.Invoke(this, new VoiceTranscriptEventArgs(final));
        ListeningStopped?.Invoke(this, new VoiceStateChangedEventArgs(
            VoiceAssistantState.Listening,
            VoiceAssistantState.Idle));

        return Task.FromResult(Result.Success());
    }

    public Task<Result> CancelListeningAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsListening = false;
        return Task.FromResult(CancelResult);
    }

    private VoiceRecognitionResult Raise(string text, bool isFinal)
    {
        var result = new VoiceRecognitionResult(text, isFinal, 1.0, DateTimeOffset.UtcNow);

        if (!isFinal)
        {
            _partials.Add(text);
        }

        PartialTranscriptReceived?.Invoke(this, new VoiceTranscriptEventArgs(result));
        return result;
    }

    private void Fail(string errorCode, string message)
    {
        LastFailureCode = errorCode;
        RecognitionFailed?.Invoke(
            this,
            new VoiceRecognitionFailedEventArgs(errorCode, message, isRecoverable: true));
    }
}

/// <summary>Stands in for the synthesizer and records what it was asked to say.</summary>
public sealed class FakeSpeechSynthesisService : ISpeechSynthesisService
{
    public bool IsAvailable { get; set; } = true;

    public bool IsSpeaking { get; private set; }

    public IReadOnlyCollection<string> AvailableVoices { get; } = ["Fake Voice"];

    public List<string> Spoken { get; } = [];

    public SpeechSynthesisOptions? LastOptions { get; private set; }

    public Result SpeakResult { get; set; } = Result.Success();

    public int StopCallCount { get; private set; }

    /// <summary>
    /// Gets or sets a transform applied to the text before it is recorded, so a test can prove
    /// the pipeline truncates or otherwise rewrites a reply before it is spoken.
    /// </summary>
    public Func<string, string>? SpeakTransform { get; set; }

    public Task<Result> SpeakAsync(
        string text,
        CancellationToken cancellationToken = default) =>
        SpeakAsync(text, new SpeechSynthesisOptions(), cancellationToken);

    public Task<Result> SpeakAsync(
        string text,
        SpeechSynthesisOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastOptions = options;

        if (SpeakResult.IsFailure)
        {
            return Task.FromResult(SpeakResult);
        }

        Spoken.Add(SpeakTransform is null ? text : SpeakTransform(text));
        IsSpeaking = true;
        return Task.FromResult(Result.Success());
    }

    public Task<Result> StopSpeakingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StopCallCount++;
        IsSpeaking = false;
        return Task.FromResult(Result.Success());
    }
}

/// <summary>
/// Grants capabilities by default and records what was asked for, so a test can assert both
/// that a handler checked and that a denial was honoured.
/// </summary>
public sealed class FakePermissionService : IPermissionService
{
    public HashSet<PermissionCapability> Denied { get; } = [];

    public List<PermissionCapability> Requested { get; } = [];

    public bool IsGranted(PermissionCapability capability) => !Denied.Contains(capability);

    public string GetDeniedMessage(PermissionCapability capability) =>
        $"The {capability} permission has not been granted.";

    public Result EnsureGranted(PermissionCapability capability)
    {
        Requested.Add(capability);
        return IsGranted(capability) ? Result.Success() : Result.Failure(GetDeniedMessage(capability));
    }
}

/// <summary>An in-memory master volume, so volume commands can be asserted precisely.</summary>
public sealed class FakeVolumeService : IVolumeService
{
    public FakeVolumeService(int volume = 50, bool isMuted = false)
    {
        Volume = volume;
        IsMuted = isMuted;
    }

    /// <summary>Gets or sets the current level, so a test can start from a known value.</summary>
    public int Volume { get; set; }

    public bool IsMuted { get; private set; }

    public int Step { get; set; } = 10;

    public Task<Result<VolumeStatus>> GetStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<VolumeStatus>.Success(Snapshot()));

    public Task<Result<VolumeStatus>> SetVolumeAsync(
        int volume,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Volume = Math.Clamp(volume, 0, 100);
        return Task.FromResult(Result<VolumeStatus>.Success(Snapshot()));
    }

    public Task<Result<VolumeStatus>> IncreaseAsync(CancellationToken cancellationToken = default) =>
        SetVolumeAsync(Volume + Step, cancellationToken);

    public Task<Result<VolumeStatus>> DecreaseAsync(CancellationToken cancellationToken = default) =>
        SetVolumeAsync(Volume - Step, cancellationToken);

    public Task<Result<VolumeStatus>> SetMutedAsync(
        bool isMuted,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsMuted = isMuted;
        return Task.FromResult(Result<VolumeStatus>.Success(Snapshot()));
    }

    private VolumeStatus Snapshot() => new(Volume, IsMuted);
}

/// <summary>Records resolution requests instead of touching the Start menu.</summary>
public sealed class FakeApplicationResolver : IApplicationResolver
{
    public FakeApplicationResolver(params string[] known)
    {
        Known = known;
    }

    public string[] Known { get; }

    public List<string> Requests { get; } = [];

    public Task<Result<ApplicationTarget>> ResolveAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(name);

        var match = Known.FirstOrDefault(known =>
            string.Equals(known, name, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(match is null
            ? Result<ApplicationTarget>.Failure($"No application matched '{name}'.")
            : Result<ApplicationTarget>.Success(
                new ApplicationTarget(match, match, ApplicationTargetKind.FilePath)));
    }
}

/// <summary>A fixed clock, so time-based responses can be asserted.</summary>
public sealed class FakeDateTimeProvider(DateTimeOffset now) : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = now;
}
