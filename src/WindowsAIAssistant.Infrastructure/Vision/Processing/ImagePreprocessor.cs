using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models.Vision;
using WindowsAIAssistant.Infrastructure.Configuration.Options;
using WindowsAIAssistant.Infrastructure.Vision.Capture;
using IImagePreprocessor = WindowsAIAssistant.Core.Abstractions.Vision.IImagePreprocessor;

namespace WindowsAIAssistant.Infrastructure.Vision.Processing;

/// <summary>
/// Crops and reduces a screenshot before anything else is allowed to look at it.
/// <para>
/// This is the only place that knows how large an image is allowed to be, and it is the reason
/// the limit cannot drift: a provider's ceiling and this one are the same number, expressed
/// once, and a request that would exceed it is made smaller rather than refused. Refusing
/// because a 4K screenshot is eight megabytes would be answering a question nobody asked.
/// </para>
/// <para>
/// Lossy compression is deliberately not used. The image is a screenshot, the small text in it
/// is the reason it was taken, and compression artefacts fall hardest on exactly those glyphs.
/// Reducing by scaling is also the only lever available, since the Windows imaging encoder in
/// this platform can be told which codec to write but not what quality to write it at — so a
/// compression setting here would be one that silently did nothing.
/// </para>
/// <para>
/// Both methods return a new value rather than mutating the bytes they are given. The original
/// is what the person sees in the preview and what gets written if they ask for a file, and a
/// preview that quietly became half-size would be a lie about their own screen.
/// </para>
/// </summary>
public sealed class ImagePreprocessor : IImagePreprocessor
{
    /// <summary>
    /// How much of the image is removed by each attempt at fitting the byte ceiling.
    /// <para>
    /// By area, not by the long edge, so the aspect ratio survives. A screenshot squeezed
    /// horizontally reads as bold text, and a recogniser or a model reading distorted glyphs
    /// will report different words than were on the screen.
    /// </para>
    /// </summary>
    private const double ReductionFactor = 0.75d;

    private readonly IOptionsMonitor<VisionOptions> _options;
    private readonly ILogger<ImagePreprocessor> _logger;

    public ImagePreprocessor(
        IOptionsMonitor<VisionOptions> options,
        ILogger<ImagePreprocessor> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Prepares an image for sending, scaled down and re-encoded only if it has to be.
    /// </summary>
    /// <param name="imageBytes">The encoded image.</param>
    /// <param name="width">The image's width in pixels.</param>
    /// <param name="height">The image's height in pixels.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// The image to use, and what had to be done to it. The flags are what let a person be told
    /// that small text may be missing, rather than discovering it by noticing that a serial
    /// number cannot be read.
    /// </returns>
    /// <exception cref="ImageProcessingException">
    /// Thrown when the image cannot be read, or when it is still over the ceiling after every
    /// permitted reduction. Carries a code from <see cref="ErrorCodes"/>.
    /// </exception>
    public async Task<PreparedImage> PrepareAsync(
        ReadOnlyMemory<byte> imageBytes,
        int width,
        int height,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        return await FitAsync(imageBytes, width, height, crop: null, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Crops a rectangle out of an encoded image and prepares what is left.
    /// </summary>
    /// <param name="imageBytes">The frame to crop.</param>
    /// <param name="frameWidth">The frame's width in pixels.</param>
    /// <param name="frameHeight">The frame's height in pixels.</param>
    /// <param name="region">The rectangle, in this frame's pixels.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The cropped and prepared image.</returns>
    /// <exception cref="ImageProcessingException">
    /// Thrown when the rectangle cannot be applied. Refused rather than approximated, so that a
    /// highlight drawn over the wrong thing cannot be answered as though it were over the right
    /// one.
    /// </exception>
    public async Task<PreparedImage> CropAsync(
        ReadOnlyMemory<byte> imageBytes,
        int frameWidth,
        int frameHeight,
        ScreenRegion region,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameHeight);

        if (!ScreenRegion.TryNormalize(region, frameWidth, frameHeight, out var normalized, out var error)
            || normalized is null)
        {
            throw new ImageProcessingException(
                error == ErrorCodes.ScreenRegionEmpty
                    ? "The part of the screenshot you selected has no pixels in it."
                    : "That part of the screenshot is outside the image.",
                error ?? ErrorCodes.ScreenRegionInvalid);
        }

        return await FitAsync(imageBytes, frameWidth, frameHeight, normalized, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Brings an image inside both limits, or explains that it cannot be.
    /// <para>
    /// An image that is already inside both limits is returned untouched — same bytes, same
    /// flags. The common case is the one where nothing needs doing, and re-encoding it anyway
    /// would cost a second decode and encode and would mark a perfectly good screenshot as
    /// re-encoded, which is a claim about somebody's screen that is not true.
    /// </para>
    /// <para>
    /// That shortcut is available only when there is nothing to crop. A crop changes the pixels,
    /// so an image that is comfortably inside the limits still has to be decoded; short-cutting
    /// it would hand back the whole frame while reporting the region's size, which tells a
    /// recogniser and the person both that nothing outside the selection was read.
    /// </para>
    /// </summary>
    private async Task<PreparedImage> FitAsync(
        ReadOnlyMemory<byte> imageBytes,
        int width,
        int height,
        ScreenRegion? crop,
        CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;

        if (imageBytes.IsEmpty)
        {
            throw new ImageProcessingException(
                "There was no image to prepare.",
                ErrorCodes.ScreenCaptureEmpty);
        }

        if (crop is null
            && imageBytes.Length <= Limit(options.MaxImageBytes)
            && width <= options.MaxImageDimension
            && height <= options.MaxImageDimension)
        {
            return new PreparedImage(imageBytes.ToArray(), width, height);
        }

        var currentWidth = crop?.Width ?? width;
        var currentHeight = crop?.Height ?? height;

        // No scaling is asked for here, not even to the crop's own size. The decoder turns a
        // region into its own dimensions on its own, and asking WIC to scale as well produces a
        // compound transform that rejects regions whose origin is not the frame's, so every
        // selected-off-center region would fail. When the result is too large, the loop below
        // scales it in a separate pass, where the transform has nothing but a scale.
        var (pixels, decodedWidth, decodedHeight) = await CapturedImageConverter
            .DecodeBgraAsync(
                imageBytes,
                crop,
                scaledWidth: 0,
                scaledHeight: 0,
                cancellationToken)
            .ConfigureAwait(false);

        var wasResized = decodedWidth != currentWidth || decodedHeight != currentHeight;
        var encoded = await CapturedImageConverter
            .EncodePngAsync(pixels, decodedWidth, decodedHeight, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var attempts = 0;
            var current = encoded;

            while (Limit(options.MaxImageBytes) is { } ceiling
                   && current.Length > ceiling
                   && attempts < options.MaxScaleAttempts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempts++;

                // Reduced by area, so the shape of the image is preserved. Reducing the long edge
                // alone would leave the other edge untouched and quietly change the aspect ratio.
                var scale = ReductionFactor;
                var nextWidth = Math.Max(1, (int)Math.Round(decodedWidth * scale));
                var nextHeight = Math.Max(1, (int)Math.Round(decodedHeight * scale));

                if (nextWidth == decodedWidth && nextHeight == decodedHeight)
                {
                    break;
                }

                var reduced = await CapturedImageConverter
                    .DecodeBgraAsync(
                        current,
                        crop: null,
                        scaledWidth: (uint)nextWidth,
                        scaledHeight: (uint)nextHeight,
                        cancellationToken)
                    .ConfigureAwait(false);

                var previous = current;
                current = await CapturedImageConverter
                    .EncodePngAsync(reduced.Pixels, reduced.Width, reduced.Height, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                decodedWidth = reduced.Width;
                decodedHeight = reduced.Height;
                wasResized = true;
                previous.AsSpan().Clear();
            }

            if (Limit(options.MaxImageBytes) is { } finalCeiling && current.Length > finalCeiling)
            {
                throw new ImageProcessingException(
                    "That screenshot is too large to send even after making it smaller. "
                    + "Selecting a smaller part of it would work.",
                    ErrorCodes.ScreenImageTooLarge);
            }

            if (wasResized)
            {
                _logger.LogInformation(
                    "A screenshot was reduced to {Width}x{Height} and {Bytes} bytes before being sent.",
                    decodedWidth,
                    decodedHeight,
                    current.Length);
            }

            return new PreparedImage(
                current,
                decodedWidth,
                decodedHeight,
                wasResized,
                wasReencoded: true);
        }
        catch
        {
            encoded.AsSpan().Clear();
            throw;
        }
    }

    /// <summary>
    /// Reads a byte ceiling, treating zero as "no limit" rather than as "nothing fits".
    /// </summary>
    private static int? Limit(int configured) => configured > 0 ? configured : null;
}
