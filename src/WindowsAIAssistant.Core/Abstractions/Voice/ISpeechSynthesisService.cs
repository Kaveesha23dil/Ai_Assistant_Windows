using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Core.Abstractions.Voice;

/// <summary>
/// Turns assistant text into spoken audio.
/// <para>
/// The interface exposes only normalized numeric tuning, never a provider voice object, so
/// Core stays independent of any speech engine.
/// </para>
/// </summary>
public interface ISpeechSynthesisService
{
    /// <summary>Gets a value indicating whether audio is currently playing.</summary>
    bool IsSpeaking { get; }

    /// <summary>Gets a value indicating whether synthesis can run on this machine right now.</summary>
    bool IsAvailable { get; }

    /// <summary>Gets the display names of the voices the platform offers.</summary>
    IReadOnlyCollection<string> AvailableVoices { get; }

    /// <summary>Speaks <paramref name="text"/> using the configured defaults.</summary>
    Task<Result> SpeakAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Speaks <paramref name="text"/> using the supplied voice, rate, pitch, and volume.</summary>
    Task<Result> SpeakAsync(
        string text,
        SpeechSynthesisOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>Stops playback immediately. Safe to call when nothing is playing.</summary>
    Task<Result> StopSpeakingAsync(CancellationToken cancellationToken = default);
}
