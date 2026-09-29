using WindowsAIAssistant.Core.Models.Vision;

namespace WindowsAIAssistant.Core.Abstractions.Vision;

/// <summary>
/// Prepares a frame before anything else looks at it: crops, scales, and encodes.
/// <para>
/// In its own interface so that the size limit a provider imposes and the size limit a
/// credential imposes cannot drift apart, and so that a test can assert a crop's exact
/// dimensions without a graphics device anywhere in sight.
/// </para>
/// </summary>
public interface IImagePreprocessor
{
    /// <summary>
    /// Encodes an image for sending, scaled down and re-encoded if it exceeds the configured
    /// limits.
    /// <para>
    /// Returns a value rather than mutating its input, because the original is what the person
    /// sees in the preview and what gets saved if they ask: a preview that quietly became
    /// half-size because it was about to be uploaded would be a lie about their own screen.
    /// </para>
    /// </summary>
    /// <exception cref="ImageProcessingException">
    /// Thrown when the image cannot be read, or when it is still over the size limit after
    /// scaling. Carries a code from <see cref="Common.ErrorCodes"/>.
    /// </exception>
    Task<PreparedImage> PrepareAsync(
        ReadOnlyMemory<byte> imageBytes,
        int width,
        int height,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Crops a rectangle out of an encoded image and encodes the result.
    /// </summary>
    /// <param name="imageBytes">The frame to crop.</param>
    /// <param name="frameWidth">The frame's width in pixels.</param>
    /// <param name="frameHeight">The frame's height in pixels.</param>
    /// <param name="region">The rectangle, in this frame's pixels.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <exception cref="ImageProcessingException">
    /// Thrown when the rectangle cannot be applied, with
    /// <see cref="Common.ErrorCodes.ScreenRegionOutsideFrame"/> or
    /// <see cref="Common.ErrorCodes.ScreenRegionInvalid"/>. Cropping is refused rather than
    /// approximated, so a highlight over the wrong thing cannot be answered as though it were
    /// over the right one.
    /// </exception>
    Task<PreparedImage> CropAsync(
        ReadOnlyMemory<byte> imageBytes,
        int frameWidth,
        int frameHeight,
        ScreenRegion region,
        CancellationToken cancellationToken = default);
}
