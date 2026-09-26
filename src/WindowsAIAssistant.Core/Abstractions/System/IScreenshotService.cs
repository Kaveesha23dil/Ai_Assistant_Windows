using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Core.Abstractions.System;

/// <summary>
/// Captures the primary display to an image file on disk.
/// <para>
/// The result is a local file path and nothing more. Screen content is never forwarded
/// anywhere: analyzing a capture is a separate capability with its own consent switch.
/// </para>
/// </summary>
public interface IScreenshotService
{
    /// <summary>Captures the primary display and saves it to the configured location.</summary>
    Task<Result<ScreenshotResult>> CaptureAsync(CancellationToken cancellationToken = default);
}
