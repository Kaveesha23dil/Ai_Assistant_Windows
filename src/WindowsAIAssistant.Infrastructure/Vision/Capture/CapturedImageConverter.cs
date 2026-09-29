using Windows.Security.Cryptography;
using Windows.Storage.Streams;

// The WinRT namespaces, named absolutely. This project has a WindowsAIAssistant.Infrastructure.Windows
// namespace of its own, and inside this namespace chain a plain "Windows.Graphics" binds to that
// one instead of the platform's.
using global::Windows.Graphics.Imaging;
using ErrorCodes = WindowsAIAssistant.Core.Common.ErrorCodes;
using ImageProcessingException = WindowsAIAssistant.Core.Exceptions.ImageProcessingException;
using ScreenRegion = WindowsAIAssistant.Core.Models.Vision.ScreenRegion;

namespace WindowsAIAssistant.Infrastructure.Vision.Capture;

/// <summary>
/// Turns a captured frame into an encoded image, crops and scales encoded images, and reads the
/// size of an encoded image.
/// <para>
/// One class for every direction because they have to agree. A capture is read as rows of
/// blue-first bytes, cropped by <see cref="CapturedFrame.ExtractRegion"/>, and handed here to be
/// encoded; a region is applied again, this time to the encoded image, by the preprocessor. Two
/// places that each decided for themselves what a byte was would disagree exactly once a crop
/// was involved, and an image that is correct until it is cropped looks like a rendering bug
/// rather than a byte-order bug.
/// </para>
/// <para>
/// Everything is encoded as lossless PNG. A screenshot is flat colour and thin text, and the
/// only reason anybody captured it is the small text in it, so an image that is made smaller by
/// being re-encoded lossily is one whose least useful pixels are the first to go. Size is
/// managed by scaling instead, which is also the only lever this platform's imaging encoder
/// actually offers: it can be told which codec to write, and not what quality to write it at.
/// </para>
/// </summary>
public static class CapturedImageConverter
{
    /// <summary>
    /// The first byte of every PNG. Checked on the way out, so an encoder that quietly produced
    /// something else fails here rather than three layers later inside a model that has no idea
    /// what it was handed.
    /// </summary>
    private const byte PngSignature = 0x89;

    /// <summary>
    /// Assumed pixel density for an encoded frame.
    /// <para>
    /// A capture has no meaningful dots-per-inch. Ninety-six is the neutral value, so a renderer
    /// that reads one from image metadata shows the frame at its own size rather than at some
    /// fraction of the physical display it came from.
    /// </para>
    /// </summary>
    private const double DefaultDpi = 96d;

    /// <summary>
    /// Encodes blue-first, eight-bit-per-channel pixels as a PNG.
    /// </summary>
    /// <param name="pixels">
    /// The frame's pixels, either tightly packed or carrying the padding Direct3D inserts
    /// between rows. Both are accepted, so a caller that has the pitch passes it and one that
    /// has a tightly packed buffer does not have to.
    /// </param>
    /// <param name="width">The image width in pixels.</param>
    /// <param name="height">The image height in pixels.</param>
    /// <param name="rowPitch">The number of bytes between rows, or zero when tightly packed.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The encoded PNG.</returns>
    /// <exception cref="ImageProcessingException">
    /// Thrown when the buffer is not the size the dimensions imply, or the imaging pipeline
    /// produced nothing. Carries a code from <see cref="ErrorCodes"/>.
    /// </exception>
    public static async Task<byte[]> EncodePngAsync(
        ReadOnlyMemory<byte> pixels,
        int width,
        int height,
        int rowPitch = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var packed = Pack(pixels.Span, width, height, rowPitch);
        cancellationToken.ThrowIfCancellationRequested();

        return await EncodeAsync(packed, width, height, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Encodes a frame already read into a managed buffer.
    /// </summary>
    /// <param name="frame">The frame to encode. Not disposed by this call.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The encoded PNG.</returns>
    public static Task<byte[]> EncodePngAsync(
        CapturedFrame frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var packed = Pack(frame.Pixels, frame.Width, frame.Height, frame.RowPitch);
        return EncodeAsync(packed, frame.Width, frame.Height, cancellationToken);
    }

    /// <summary>
    /// Reads the size of an encoded image without decoding its pixels.
    /// </summary>
    /// <param name="imageBytes">The encoded image.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The width and height in pixels.</returns>
    /// <exception cref="ImageProcessingException">
    /// Thrown when the bytes are not an image this machine can read.
    /// </exception>
    public static async Task<(int Width, int Height)> ReadSizeAsync(
        ReadOnlyMemory<byte> imageBytes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var stream = await ToStreamAsync(imageBytes, cancellationToken).ConfigureAwait(false);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask().ConfigureAwait(false);

        return (checked((int)decoder.PixelWidth), checked((int)decoder.PixelHeight));
    }

    /// <summary>
    /// Decodes an encoded image, optionally cropping and scaling it in the same pass.
    /// <para>
    /// The geometry is handed to the imaging pipeline rather than applied to decoded pixels
    /// afterwards, so cropping never means decoding a full 4K frame only to copy a corner out
    /// of it, and scaling never means resampling in managed code. The same call is used for
    /// both, which is why there is only one crop path in the whole application.
    /// </para>
    /// </summary>
    /// <param name="imageBytes">The encoded image to read.</param>
    /// <param name="crop">
    /// The rectangle to keep, in the source image's own pixels, or <see langword="null"/> to
    /// keep all of it.
    /// </param>
    /// <param name="scaledWidth">The width to produce, or zero to keep the cropped width.</param>
    /// <param name="scaledHeight">The height to produce, or zero to keep the cropped height.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The decoded pixels and their size.</returns>
    /// <exception cref="ImageProcessingException">
    /// Thrown when the image cannot be read, or when the requested size is not usable.
    /// </exception>
    public static async Task<(byte[] Pixels, int Width, int Height)> DecodeBgraAsync(
        ReadOnlyMemory<byte> imageBytes,
        ScreenRegion? crop = null,
        uint scaledWidth = 0,
        uint scaledHeight = 0,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (crop is { } region && (region.IsEmpty || region.X < 0 || region.Y < 0))
        {
            throw new ImageProcessingException(
                "The selected part of the screenshot is empty.",
                ErrorCodes.ScreenRegionEmpty);
        }

        using var stream = await ToStreamAsync(imageBytes, cancellationToken).ConfigureAwait(false);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask().ConfigureAwait(false);

        BitmapTransform? transform = null;

        if (crop is not null || scaledWidth > 0 || scaledHeight > 0)
        {
            transform = new BitmapTransform
            {
                // Linear rather than the default: a screenshot is mostly text, and the default
                // filter blurs small glyphs into their neighbours, which is exactly the
                // information the capture was taken to obtain.
                InterpolationMode = BitmapInterpolationMode.Linear,
            };

            if (crop is { } bounds)
            {
                transform.Bounds = new BitmapBounds
                {
                    X = (uint)bounds.X,
                    Y = (uint)bounds.Y,
                    Width = (uint)bounds.Width,
                    Height = (uint)bounds.Height,
                };
            }

            if (scaledWidth > 0)
            {
                transform.ScaledWidth = scaledWidth;
            }

            if (scaledHeight > 0)
            {
                transform.ScaledHeight = scaledHeight;
            }
        }

        SoftwareBitmap? software = null;

        try
        {
            software = transform is null
                ? await decoder
                    .GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied)
                    .AsTask()
                    .ConfigureAwait(false)
                : await decoder
                    .GetSoftwareBitmapAsync(
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Premultiplied,
                        transform,
                        ExifOrientationMode.IgnoreExifOrientation,
                        ColorManagementMode.DoNotColorManage)
                    .AsTask()
                    .ConfigureAwait(false);

            if (software.PixelWidth <= 0 || software.PixelHeight <= 0)
            {
                throw new ImageProcessingException(
                    "The screenshot had no pixels in it.",
                    ErrorCodes.ScreenCaptureEmpty);
            }

            var packed = new byte[software.PixelWidth * software.PixelHeight * 4];

            // Through a cryptographic buffer rather than a marshalled pointer: the projection
            // exposes no direct path from a managed array to a software bitmap, and the buffer
            // owns its copy, so nothing here needs a byte array pinned past its own call.
            software.CopyToBuffer(CryptographicBuffer.CreateFromByteArray(packed));

            return (packed, software.PixelWidth, software.PixelHeight);
        }
        catch (ImageProcessingException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A codec that refuses an input reports it as a COM failure, and a person needs the
            // same sentence whichever encoder or codec version produced it.
            throw new ImageProcessingException(
                "The screenshot could not be read.",
                ErrorCodes.ScreenImageInvalid,
                exception);
        }
        finally
        {
            software?.Dispose();
        }
    }

    /// <summary>
    /// Copies a possibly row-padded buffer into a tightly packed one.
    /// <para>
    /// A pitch smaller than a row is replaced with the packed one rather than refused, because a
    /// caller that passed zero means "tightly packed", and a caller that misread a Direct3D row
    /// pitch would otherwise be walked backwards through its own buffer. A buffer too small for
    /// its stated size is refused, because there is no safe reading of that.
    /// </para>
    /// </summary>
    private static byte[] Pack(ReadOnlySpan<byte> pixels, int width, int height, int rowPitch)
    {
        if (rowPitch < 0)
        {
            throw new ImageProcessingException(
                "The captured frame described its rows backwards.",
                ErrorCodes.ScreenImageInvalid);
        }

        var rowBytes = width * 4;
        var required = rowBytes * height;

        if (pixels.Length < required)
        {
            throw new ImageProcessingException(
                "The captured frame held fewer bytes than its size needs.",
                ErrorCodes.ScreenImageInvalid);
        }

        var actualPitch = rowPitch < rowBytes ? rowBytes : rowPitch;

        if (actualPitch == rowBytes)
        {
            return pixels[..required].ToArray();
        }

        var packed = new byte[required];

        for (var row = 0; row < height; row++)
        {
            var sourceStart = row * actualPitch;
            pixels.Slice(sourceStart, rowBytes).CopyTo(packed.AsSpan(row * rowBytes, rowBytes));
        }

        return packed;
    }

    /// <summary>
    /// Runs the Windows PNG encoder over tightly packed pixels.
    /// </summary>
    private static async Task<byte[]> EncodeAsync(
        byte[] packed,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            CryptographicBuffer.CreateFromByteArray(packed),
            BitmapPixelFormat.Bgra8,
            width,
            height,
            // Premultiplied rather than straight, because the software bitmap is the only format
            // the encoder accepts here and premultiplied is what a capture's pixels are.
            BitmapAlphaMode.Premultiplied);

        return await EncodeSoftwareBitmapAsync(bitmap, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Encodes a software bitmap as a PNG and returns the bytes.
    /// </summary>
    private static async Task<byte[]> EncodeSoftwareBitmapAsync(
        SoftwareBitmap bitmap,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var stream = new InMemoryRandomAccessStream();

        try
        {
            // The encoder is created over the stream it will write into rather than attached
            // afterwards, which is the only order the Windows encoder supports.
            var encoder = await BitmapEncoder
                .CreateAsync(BitmapEncoder.PngEncoderId, stream)
                .AsTask()
                .ConfigureAwait(false);

            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync().AsTask().ConfigureAwait(false);

            if (stream.Size == 0)
            {
                throw new ImageProcessingException(
                    "The imaging pipeline produced no image data.",
                    ErrorCodes.ScreenImageEncodeFailed);
            }

            stream.Seek(0);

            using var reader = new DataReader(stream.GetInputStreamAt(0));
            var loaded = await reader.LoadAsync((uint)stream.Size).AsTask().ConfigureAwait(false);

            var encoded = new byte[loaded];
            reader.ReadBytes(encoded);

            if (encoded.Length == 0 || encoded[0] != PngSignature)
            {
                throw new ImageProcessingException(
                    "The imaging pipeline did not return an image in the expected format.",
                    ErrorCodes.ScreenImageEncodeFailed);
            }

            return encoded;
        }
        catch (ImageProcessingException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A codec that refuses an input reports it as a COM failure, and a person needs the
            // same sentence whichever encoder or codec version produced it.
            throw new ImageProcessingException(
                "The screenshot could not be encoded.",
                ErrorCodes.ScreenImageEncodeFailed,
                exception);
        }
    }

    /// <summary>
    /// Wraps bytes in the stream shape the imaging pipeline reads from.
    /// <para>
    /// Asynchronous throughout, because the alternative is a blocking wait on a call that can
    /// complete on a captured synchronization context, which is the shape of a deadlock rather
    /// than a convenience.
    /// </para>
    /// </summary>
    private static async Task<IRandomAccessStream> ToStreamAsync(
        ReadOnlyMemory<byte> imageBytes,
        CancellationToken cancellationToken)
    {
        if (imageBytes.IsEmpty)
        {
            throw new ImageProcessingException(
                "There was no image to read.",
                ErrorCodes.ScreenCaptureEmpty);
        }

        var stream = new InMemoryRandomAccessStream();

        try
        {
            using var writer = new DataWriter(stream.GetOutputStreamAt(0));
            writer.WriteBytes(imageBytes.Span.ToArray());
            await writer.StoreAsync().AsTask().ConfigureAwait(false);
            writer.DetachStream();

            stream.Seek(0);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
}
