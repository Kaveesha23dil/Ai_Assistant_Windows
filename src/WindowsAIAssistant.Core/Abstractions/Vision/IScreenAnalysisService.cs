using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Vision;

namespace WindowsAIAssistant.Core.Abstractions.Vision;

/// <summary>
/// Runs a whole visual request: capture, read, analyse, and hand back an answer.
/// <para>
/// One interface in front of the overlay, the chat, and the voice handler, so that the consent
/// decisions are made once, in one place, no matter who asked. Every path goes through the same
/// sequence, which is what makes "the same picture cannot be sent from the keyboard and not sent
/// from the microphone" a property of the design rather than a promise.
/// </para>
/// </summary>
public interface IScreenAnalysisService
{
    /// <summary>
    /// Takes a screenshot and looks at it.
    /// <para>
    /// Cancels cleanly: a cancelled request leaves the assistant idle and raises nothing.
    /// </para>
    /// </summary>
    Task<Result<ScreenAnalysisResult>> AnalyzeScreenAsync(
        ScreenAnalysisRequestOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the text on screen, locally, with no model and no network.
    /// </summary>
    Task<Result<ScreenAnalysisResult>> ReadScreenTextAsync(
        ScreenAnalysisRequestOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>Reports what this machine can do, without capturing anything.</summary>
    Task<VisionCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default);
}
