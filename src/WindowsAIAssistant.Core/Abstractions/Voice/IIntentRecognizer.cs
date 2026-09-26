using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Core.Abstractions.Voice;

/// <summary>
/// Converts a transcript into an intent. Implementations are deterministic and local so the
/// common commands never pay for a model round trip; an AI fallback can be layered on later.
/// </summary>
public interface IIntentRecognizer
{
    /// <summary>Gets the intents this recognizer can produce.</summary>
    IReadOnlyCollection<AssistantIntent> SupportedIntents { get; }

    /// <summary>
    /// Attempts to map <paramref name="transcript"/> to a command.
    /// </summary>
    /// <param name="transcript">The text the user produced.</param>
    /// <param name="recognitionConfidence">
    /// The recognizer confidence between 0 and 1. It is multiplied into the matcher
    /// confidence so a poor match combined with a poor transcript can drop below the
    /// confirmation threshold.
    /// </param>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    /// <returns>The recognized command, or a failure carrying the not-recognized error code.</returns>
    Task<Result<VoiceCommand>> RecognizeAsync(
        string transcript,
        double recognitionConfidence = 1.0,
        CancellationToken cancellationToken = default);
}
