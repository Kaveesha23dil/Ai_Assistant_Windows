using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Core.Abstractions.Voice;

/// <summary>
/// Optional always-on wake phrase detection.
/// <para>
/// The feature is disabled by default. An implementation must run entirely on the local
/// machine: the contract deliberately offers no way to stream microphone audio anywhere, so
/// a cloud wake-word engine cannot be plugged in through this surface.
/// </para>
/// </summary>
public interface IWakeWordService
{
    /// <summary>Gets a value indicating whether wake phrase detection is switched on.</summary>
    bool IsEnabled { get; }

    /// <summary>Gets the wake phrases this detector listens for.</summary>
    IReadOnlyCollection<string> WakePhrases { get; }

    /// <summary>Gets a value indicating whether a local detector is installed.</summary>
    bool IsAvailable { get; }

    /// <summary>Raised when a wake phrase is detected in the local audio stream.</summary>
    event EventHandler<WakeWordMatch>? WakeWordDetected;

    /// <summary>Begins local detection. Fails when the detector is disabled or unavailable.</summary>
    Task<Result> StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops local detection.</summary>
    Task<Result> StopAsync(CancellationToken cancellationToken = default);
}
