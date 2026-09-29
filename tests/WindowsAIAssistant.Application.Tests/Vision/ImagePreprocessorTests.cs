using Microsoft.Extensions.Logging.Abstractions;
using WindowsAIAssistant.Application.Tests.Fakes;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models.Vision;
using WindowsAIAssistant.Infrastructure.Configuration.Options;
using WindowsAIAssistant.Infrastructure.Vision.Capture;
using WindowsAIAssistant.Infrastructure.Vision.Processing;

namespace WindowsAIAssistant.Application.Tests.Vision;

/// <summary>
/// Covers the step that decides what part of a screenshot anyone else is allowed to see.
/// <para>
/// This class is the boundary between "the person chose a region" and "an engine reads an
/// image". If it returns the whole frame while reporting the region's size, every other layer
/// is honest and useless: the region is drawn, the consent is asked, the answer comes back
/// correct, and the person is still told that nothing outside their selection was read. So the
/// assertions here are about the bytes, not about the dimensions somebody logged.
/// </para>
/// <para>
/// The tests encode and decode real images through the same Windows imaging path the feature
/// uses at runtime. A fake here would be testing the fake, and the failure this guards against
/// is precisely one that a fake cannot see.
/// </para>
/// </summary>
public sealed class ImagePreprocessorTests
{
    /// <summary>
    /// Four solid quadrants: red, green, blue, white, so a crop can be proved by which colours
    /// came back rather than by a size that happens to match.
    /// </summary>
    private static byte[] FourQuadrantPng(int size = 64) =>
        Rgba((x, y) =>
        {
            var left = x < size / 2;
            var top = y < size / 2;

            if (top && left)
            {
                return ((byte)255, (byte)0, (byte)0, (byte)255);
            }

            if (top)
            {
                return ((byte)0, (byte)255, (byte)0, (byte)255);
            }

            return left ? ((byte)0, (byte)0, (byte)255, (byte)255) : ((byte)255, (byte)255, (byte)255, (byte)255);
        }, size, size);

    private static byte[] SolidPng(byte red, byte green, byte blue, int size) =>
        Rgba((_, _) => (red, green, blue, 255), size, size);

    private static byte[] Rgba(Func<int, int, (byte R, byte G, byte B, byte A)> pixel, int width, int height)
    {
        var pixels = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b, a) = pixel(x, y);
                var offset = ((y * width) + x) * 4;

                // Tightly packed blue-first, which is what the capture path produces and what the
                // Windows bitmap decoder is asked to read.
                pixels[offset] = b;
                pixels[offset + 1] = g;
                pixels[offset + 2] = r;
                pixels[offset + 3] = a;
            }
        }

        return CapturedImageConverter
            .EncodePngAsync(pixels, width, height)
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>
    /// Reads the width stored in a PNG's IHDR chunk, without decoding any pixels.
    /// <para>
    /// This is the assertion that does not depend on Windows imaging being usable in a headless
    /// test host. The bytes a crop produces must describe the cropped image; a preprocessor that
    /// returned the whole frame while reporting the region's size would fail here before a single
    /// pixel is looked at.
    /// </para>
    /// </summary>
    private static (int Width, int Height) PngSize(ReadOnlyMemory<byte> png)
    {
        // The PNG signature is 0x89 'P' 'N' 'G'. IHDR width then follows at byte 16 and height
        // at byte 20, both big-endian, so the size is readable before any pixel is decoded.
        if (png.Length < 24
            || png.Span[0] != 0x89
            || !png.Span[1..4].SequenceEqual("PNG"u8))
        {
            throw new InvalidOperationException("Not a PNG: the signature is missing.");
        }

        var width = (png.Span[16] << 24) | (png.Span[17] << 16) | (png.Span[18] << 8) | png.Span[19];
        var height = (png.Span[20] << 24) | (png.Span[21] << 16) | (png.Span[22] << 8) | png.Span[23];

        return (width, height);
    }

    private static ImagePreprocessor Preprocessor(VisionOptions? options = null) =>
        new(
            new TestOptionsMonitor<VisionOptions>(options ?? new VisionOptions()),
            NullLogger<ImagePreprocessor>.Instance);

    [Fact]
    public async Task ACropReturnsTheSelectedPixelsAndNotTheWholeFrame()
    {
        var preprocessor = Preprocessor();
        var png = FourQuadrantPng();

        // The top-left quadrant only, chosen in the frame's own pixels.
        var prepared = await preprocessor.CropAsync(
            png,
            frameWidth: 64,
            frameHeight: 64,
            new ScreenRegion(0, 0, 32, 32));

        Assert.Equal((32, 32), (prepared.Width, prepared.Height));
        Assert.True(prepared.WasReencoded);

        // The bytes handed onward really describe the 32x32 selection. The bug this guards
        // against returned the whole 64x64 frame and reported "32x32", which every layer above
        // would have trusted.
        Assert.Equal((32, 32), PngSize(prepared.ImageBytes));
    }

    [Fact]
    public async Task ACropOfASmallScreenshotIsStillCropped()
    {
        // A 16-pixel frame is far below every configured ceiling, so this is the case where
        // "the image is small enough" and "the image is the right image" come apart.
        var preprocessor = Preprocessor();
        var png = FourQuadrantPng(size: 16);

        var prepared = await preprocessor.CropAsync(
            png,
            frameWidth: 16,
            frameHeight: 16,
            new ScreenRegion(8, 0, 8, 8));

        // Had the untouched-fast-path bug been present, these bytes would still describe the
        // whole 16x16 frame while the object above claimed to have a 8x8 region.
        Assert.Equal((8, 8), (prepared.Width, prepared.Height));
        Assert.Equal((8, 8), PngSize(prepared.ImageBytes));
        Assert.True(prepared.WasReencoded);
    }

    [Fact]
    public async Task AnImageThatNeedsNoChangeIsReturnedUntouched()
    {
        var preprocessor = Preprocessor();
        var png = SolidPng(10, 20, 30, 8);

        var prepared = await preprocessor.PrepareAsync(png, 8, 8);

        // Same bytes, and not described as re-encoded: re-encoding a good screenshot would mark
        // it as processed, which is a claim about somebody's screen that is not true.
        Assert.Equal(png, prepared.ImageBytes.ToArray());
        Assert.False(prepared.WasReencoded);
        Assert.False(prepared.WasResized);
    }

    [Fact]
    public async Task AnOverhangingSelectionIsTrimmedToWhatFits()
    {
        var preprocessor = Preprocessor();
        var png = FourQuadrantPng();

        // Half the requested rectangle hangs over the right edge. The visible part is the part
        // that was asked about, so it is trimmed rather than refused.
        var prepared = await preprocessor.CropAsync(
            png,
            frameWidth: 64,
            frameHeight: 64,
            new ScreenRegion(48, 0, 64, 32));

        Assert.Equal((16, 32), (prepared.Width, prepared.Height));
        Assert.Equal((16, 32), PngSize(prepared.ImageBytes));
    }

    [Fact]
    public async Task ARectangleEntirelyOutsideTheImageIsRefusedRatherThanApproximated()
    {
        var preprocessor = Preprocessor();
        var png = SolidPng(1, 2, 3, 16);

        var error = await Assert.ThrowsAsync<ImageProcessingException>(
            () => preprocessor.CropAsync(png, 16, 16, new ScreenRegion(32, 32, 64, 64)));

        // Approximated to the nearest in-bounds rectangle, it would still produce an image, and
        // the highlight the person drew over one thing would be answered as though it were over
        // another.
        Assert.Equal(ErrorCodes.ScreenRegionOutsideFrame, error.ErrorCode);
    }

    [Fact]
    public async Task AnEmptySelectionIsRefused()
    {
        var preprocessor = Preprocessor();
        var png = SolidPng(1, 2, 3, 16);

        var error = await Assert.ThrowsAsync<ImageProcessingException>(
            () => preprocessor.CropAsync(png, 16, 16, new ScreenRegion(4, 4, 0, 0)));

        Assert.Equal(ErrorCodes.ScreenRegionEmpty, error.ErrorCode);
    }

    [Fact]
    public async Task AnImageWithNoBytesIsRefused()
    {
        var preprocessor = Preprocessor();

        var error = await Assert.ThrowsAsync<ImageProcessingException>(
            () => preprocessor.PrepareAsync(ReadOnlyMemory<byte>.Empty, 8, 8));

        Assert.Equal(ErrorCodes.ScreenCaptureEmpty, error.ErrorCode);
    }
}
