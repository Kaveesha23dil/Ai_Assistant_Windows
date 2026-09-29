using Windows.Security.Cryptography;
using WindowsAIAssistant.Infrastructure.Vision.Capture;
using ErrorCodes = WindowsAIAssistant.Core.Common.ErrorCodes;
using ImageProcessingException = WindowsAIAssistant.Core.Exceptions.ImageProcessingException;
using ScreenRegion = WindowsAIAssistant.Core.Models.Vision.ScreenRegion;

namespace WindowsAIAssistant.Infrastructure.Vision.Ocr;

// The WinRT imaging namespace, named absolutely. This project has a
// WindowsAIAssistant.Infrastructure.Windows namespace of its own, and inside this namespace
// chain a plain "Windows.Graphics" binds to that one instead of the platform's.
using global::Windows.Graphics.Imaging;

/// <summary>
/// Turns an encoded screenshot into a software bitmap a text recogniser will accept.
/// <para>
/// Separate from <see cref="CapturedImageConverter"/> and public rather than internal, because
/// two recognisers in two assemblies need the identical preparation and a second copy of it is
/// how one engine ends up reading a 4K frame at full size while the other quietly scales it.
/// </para>
/// <para>
/// The scaling limit is the recogniser's, not the application's. Every Windows text recogniser
/// refuses an image larger than a ceiling of its own — around 2600 pixels on the long edge — and
/// hands back a failed call rather than a smaller image, so anything above that limit has to be
/// reduced before the recogniser sees it. Which is not a loss: a screenshot of a 4K display is
/// already far more detail than a text recogniser can resolve.
/// </para>
/// </summary>
public static class OcrImageFactory
{
    /// <summary>
    /// Decodes an encoded image and reduces it to fit a recogniser.
    /// </summary>
    /// <param name="imageBytes">The encoded image.</param>
    /// <param name="maximumDimension">
    /// The longest edge the recogniser will take, or zero for no limit.
    /// </param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// A software bitmap the caller disposes, already at the size the recogniser can read.
    /// </returns>
    /// <exception cref="ImageProcessingException">
    /// Thrown when the image cannot be read or has no pixels.
    /// </exception>
    public static async Task<SoftwareBitmap> CreateAsync(
        ReadOnlyMemory<byte> imageBytes,
        uint maximumDimension,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (width, height) = await CapturedImageConverter
            .ReadSizeAsync(imageBytes, cancellationToken)
            .ConfigureAwait(false);

        var longest = (uint)Math.Max(width, height);
        var needsScaling = maximumDimension > 0 && longest > maximumDimension;

        uint targetWidth = 0;
        uint targetHeight = 0;

        if (needsScaling)
        {
            // Reduced by area rather than by the long edge alone, so the aspect ratio is kept.
            // Stretching one axis to fit is a way to make text look bold and wide, and a
            // recogniser reading distorted glyphs reports different words than were there.
            var scale = maximumDimension / (double)longest;
            targetWidth = Math.Max(1u, (uint)Math.Round(width * scale));
            targetHeight = Math.Max(1u, (uint)Math.Round(height * scale));
        }

        var decoded = await CapturedImageConverter
            .DecodeBgraAsync(
                imageBytes,
                crop: null,
                scaledWidth: targetWidth,
                scaledHeight: targetHeight,
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            return SoftwareBitmap.CreateCopyFromBuffer(
                CryptographicBuffer.CreateFromByteArray(decoded.Pixels),
                BitmapPixelFormat.Bgra8,
                decoded.Width,
                decoded.Height,
                // Premultiplied, which is the one mode a recogniser will accept for a bitmap
                // built from a buffer. Straight alpha reads as a different colour.
                BitmapAlphaMode.Premultiplied);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ImageProcessingException(
                "The screenshot could not be prepared for text recognition.",
                ErrorCodes.ScreenImageInvalid,
                exception);
        }
    }

    /// <summary>
    /// Turns a rectangle a recogniser reported into the type the rest of the application uses.
    /// <para>
    /// A recogniser reports floating point bounds and can report a box of nothing for a
    /// character it could not read. Both are normalised here — rounded to whole pixels and
    /// refused if empty — so that every consumer downstream can assume its rectangles cover at
    /// least one pixel, and so that a region can be asked for without checking it first.
    /// </para>
    /// </summary>
    /// <param name="boundingBox">The rectangle as the recogniser reported it.</param>
    /// <param name="imageWidth">The width of the image the rectangle came from.</param>
    /// <param name="imageHeight">The height of the image the rectangle came from.</param>
    /// <returns>The rectangle, or <see langword="null"/> when it covers no pixels.</returns>
    public static ScreenRegion? NormalizeBounds(
        global::Windows.Foundation.Rect boundingBox,
        int imageWidth,
        int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            return null;
        }

        if (!ScreenRegion.TryNormalize(
                new ScreenRegion(
                    (int)Math.Floor(boundingBox.X),
                    (int)Math.Floor(boundingBox.Y),
                    (int)Math.Ceiling(boundingBox.Width),
                    (int)Math.Ceiling(boundingBox.Height)),
                imageWidth,
                imageHeight,
                out var region,
                out _))
        {
            return null;
        }

        return region;
    }
}
