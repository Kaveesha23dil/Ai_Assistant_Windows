using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Application.Common.Errors;
using WindowsAIAssistant.Application.Voice.Text;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Voice.Services;

/// <summary>
/// Implements the narrow control surface voice handlers use to steer the assistant.
/// <para>
/// This class exists to break a dependency cycle. The assistant owns the router, the router
/// owns the handlers, and handlers such as "stop talking" need to reach the assistant again.
/// Routing that need through this collaborator, which depends only on the shared session and
/// the two speech services, keeps the object graph acyclic.
/// </para>
/// </summary>
public sealed class VoiceAssistantControl : IVoiceAssistantControl
{
    private readonly AssistantSession _session;
    private readonly ISpeechRecognitionService _recognition;
    private readonly ISpeechSynthesisService _synthesis;
    private readonly IPermissionService _permissions;
    private readonly VoiceIntentPolicy _policy;
    private readonly ILogger<VoiceAssistantControl> _logger;

    public VoiceAssistantControl(
        AssistantSession session,
        ISpeechRecognitionService recognition,
        ISpeechSynthesisService synthesis,
        IPermissionService permissions,
        VoiceIntentPolicy policy,
        ILogger<VoiceAssistantControl> logger)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(recognition);
        ArgumentNullException.ThrowIfNull(synthesis);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(logger);

        _session = session;
        _recognition = recognition;
        _synthesis = synthesis;
        _permissions = permissions;
        _policy = policy;
        _logger = logger;
    }

    /// <inheritdoc />
    public VoiceAssistantState State => _session.State;

    /// <inheritdoc />
    public string? LastResponse => _session.LastResponse;

    /// <inheritdoc />
    public async Task<Result> SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Result.Failure("There is nothing to read.");
        }

        if (!_policy.SpeakResponses)
        {
            _logger.LogDebug("Spoken replies are disabled; response was not spoken.");
            return Result.Success();
        }

        var spoken = ResponseTextFormatter.TruncateForSpeech(text, _policy.MaximumSpokenResponseLength);

        _session.TransitionTo(VoiceAssistantState.Speaking);
        try
        {
            return await _synthesis
                .SpeakAsync(
                    spoken,
                    new SpeechSynthesisOptions
                    {
                        Language = _policy.Language,
                        MaximumSpeechLength = _policy.MaximumSpokenResponseLength
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _session.TransitionTo(VoiceAssistantState.Idle);
        }
    }

    /// <inheritdoc />
    public async Task<Result> StopSpeakingAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Voice synthesis stop requested.");

        var result = await _synthesis.StopSpeakingAsync(cancellationToken).ConfigureAwait(false);
        _session.TransitionTo(VoiceAssistantState.Idle);
        return result;
    }

    /// <inheritdoc />
    public async Task<Result> CancelAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Voice operation cancelled by the user.");

        cancellationToken.ThrowIfCancellationRequested();

        _session.CancelActiveOperation();
        _session.ClearPendingCommand();

        // The teardown below deliberately ignores the caller's token. Cancelling an operation
        // cancels the session token, and that token is what the caller passed in, so observing
        // it here would make every cancel throw before the hardware was actually stopped. A
        // cancel that leaves the microphone or the speaker running is worse than a slow one.
        var stopSpeech = await _synthesis.StopSpeakingAsync(CancellationToken.None).ConfigureAwait(false);
        var stopListening = _recognition.IsListening
            ? await _recognition.CancelListeningAsync(CancellationToken.None).ConfigureAwait(false)
            : Result.Success();

        _session.TransitionTo(VoiceAssistantState.Idle);

        return stopSpeech.IsSuccess ? stopListening : stopSpeech;
    }

    /// <inheritdoc />
    public async Task<Result> StartListeningAsync(CancellationToken cancellationToken = default)
    {
        var permission = _permissions.EnsureGranted(PermissionCapability.Microphone);
        if (permission.IsFailure)
        {
            _logger.LogInformation("Listening refused because microphone access is not granted.");
            return permission;
        }

        if (!_policy.Enabled)
        {
            return Result.Failure("The voice assistant is currently switched off.");
        }

        // Starting a recognizer while audio is playing would feed the assistant its own voice,
        // so the output is stopped first.
        if (_synthesis.IsSpeaking)
        {
            await _synthesis.StopSpeakingAsync(cancellationToken).ConfigureAwait(false);
        }

        _session.TransitionTo(VoiceAssistantState.Listening);
        return await _recognition
            .StartListeningAsync(SpeechRecognitionOptions.For(_policy.Language), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> StopListeningAsync(CancellationToken cancellationToken = default)
    {
        if (!_recognition.IsListening)
        {
            return Result.Success();
        }

        _logger.LogInformation("Voice listening stopped on request.");
        return await _recognition.StopListeningAsync(cancellationToken).ConfigureAwait(false);
    }
}
