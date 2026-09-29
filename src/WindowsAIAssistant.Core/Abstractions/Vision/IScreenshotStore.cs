using WindowsAIAssistant.Core.Common;

namespace WindowsAIAssistant.Core.Abstractions.Vision;

/// <summary>
/// Writes a frame to a file, when a person asks for one.
/// <para>
/// Separate from capture on purpose, and the split is the privacy boundary. Capture returns
/// bytes in memory and nothing else; this is the only path by which pixels reach a disk, so
/// "no screenshot is saved unless I asked for it" is a property of the type system rather than
/// a rule someone has to remember. Saving is a separate command with its own consent check.
/// </para>
/// </summary>
public interface IScreenshotStore
{
    /// <summary>
    /// Writes a captured frame to a file.
    /// </summary>
    /// <param name="capture">The frame to write. Not disposed by this call.</param>
    /// <param name="destinationFolder">
    /// The folder to write into, or <see langword="null"/> to use the visible Pictures location.
    /// </param>
    /// <param name="suggestedFileName">
    /// A name to offer, or <see langword="null"/> to generate one. Never used as a path.
    /// </param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The path written, or a failed result.</returns>
    /// <remarks>
    /// The caller's cancellation token is honoured between choosing the file and writing it, so
    /// a person who dismisses the save dialog never leaves a half-written screenshot behind.
    /// </remarks>
    Task<Result<string>> SaveAsync(
        Models.Vision.ScreenCaptureResult capture,
        string? destinationFolder,
        string? suggestedFileName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the folder screenshots are written to when no folder was named.
    /// <para>
    /// A visible location under Pictures, never a hidden application folder: a file the person
    /// cannot find is not something they can delete when they change their mind.
    /// </para>
    /// </summary>
    Task<Result<string>> GetDefaultFolderAsync(CancellationToken cancellationToken = default);
}
