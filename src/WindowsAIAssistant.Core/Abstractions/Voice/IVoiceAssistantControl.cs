using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Abstractions.Voice;

/// <summary>
/// The narrow control surface voice handlers use to steer the assistant itself.
/// <para>
/// Handlers such as "stop talking" or "repeat that" need to reach the running assistant.
/// Depending on this interface instead of <see cref="IVoiceAssistantService"/> is what keeps
/// the object graph acyclic: the assistant depends on the command router, the router depends
/// on the handlers, and the handlers depend only on this control surface and the shared
/// session state.
/// </para>
/// </summary>
public interface IVoiceAssistantControl
{
    /// <summary>Gets the current lifecycle state.</summary>
    VoiceAssistantState State { get; }

    /// <summary>Gets the most recent assistant response, or <see langword="null"/> when there is none.</summary>
    string? LastResponse { get; }

    /// <summary>Speaks text using the configured voice settings.</summary>
    Task<Result> SpeakAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Stops spoken output immediately.</summary>
    Task<Result> StopSpeakingAsync(CancellationToken cancellationToken = default);

    /// <summary>Abandons the active operation and returns the assistant to idle.</summary>
    Task<Result> CancelAsync(CancellationToken cancellationToken = default);

    /// <summary>Begins capturing speech from the microphone.</summary>
    Task<Result> StartListeningAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends an active listening session while keeping whatever was already transcribed.
    /// This is deliberately different from <see cref="CancelAsync"/>, which discards the
    /// partial transcript and the pending command as well.
    /// </summary>
    Task<Result> StopListeningAsync(CancellationToken cancellationToken = default);
}
