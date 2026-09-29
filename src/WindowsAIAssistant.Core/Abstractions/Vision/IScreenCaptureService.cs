using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Vision;

namespace WindowsAIAssistant.Core.Abstractions.Vision;

/// <summary>
/// Takes one screenshot, on request, and hands back the frame in memory.
/// <para>
/// Provider-independent: nothing here mentions a Windows API, a picker, or a session object, so
/// a test can supply a fake capture that returns three different frames in a row and an
/// Application-layer test can prove that a caller disposes each one before asking for the next.
/// </para>
/// </summary>
public interface IScreenCaptureService
{
    /// <summary>
    /// Gets a value indicating whether this machine can capture a frame at all.
    /// <para>
    /// Checked and reported rather than assumed, so a build running somewhere unexpected says
    /// so instead of throwing on the first request.
    /// </para>
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Captures one frame.
    /// <para>
    /// A person who dismisses the picker is not a failure, but the caller still has to be able to
    /// tell that apart from a fault, so the implementation reports
    /// <see cref="ErrorCodes.ScreenCaptureCancelled"/> as the code on a failed result and the
    /// caller shows nothing.
    /// </para>
    /// </summary>
    Task<Result<ScreenCaptureResult>> CaptureAsync(
        ScreenCaptureRequest request,
        CancellationToken cancellationToken = default);
}
