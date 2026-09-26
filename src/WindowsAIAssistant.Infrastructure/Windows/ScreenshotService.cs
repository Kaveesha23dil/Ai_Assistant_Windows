using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;
using WindowsAIAssistant.Infrastructure.Configuration.Options;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace WindowsAIAssistant.Infrastructure.Windows;

/// <summary>
/// Captures the primary display and writes it to a PNG file on disk.
/// <para>
/// The capture is a local GDI screen copy encoded by the Windows imaging pipeline. The result
/// is a file path and nothing more: the image is never uploaded, and nothing here has a way
/// to send screen content anywhere. Analyzing a capture is a separate capability with its
/// own consent switch, and this class has no member that would perform it.
/// </para>
/// </summary>
public sealed partial class ScreenshotService : IScreenshotService
{
    private const uint SourceCopy = 0x00CC0020;
    private const int BitsPerPixel = 32;
    private const byte PngSignature = 0x89;

    /// <summary>
    /// BI_RGB. With 32 bits per pixel the fourth byte of each pixel is left undefined by GDI,
    /// so the encoder is told to ignore the alpha channel rather than read a channel that was
    /// never written.
    /// </summary>
    private const uint BiRgb = 0;

    private readonly IKnownFolderService _knownFolders;
    private readonly IOptionsMonitor<VoiceOptions> _options;
    private readonly ILogger<ScreenshotService> _logger;

    public ScreenshotService(
        IKnownFolderService knownFolders,
        IOptionsMonitor<VoiceOptions> options,
        ILogger<ScreenshotService> logger)
    {
        ArgumentNullException.ThrowIfNull(knownFolders);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _knownFolders = knownFolders;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<ScreenshotResult>> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        byte[]? pixels = null;
        IntPtr screenDc = IntPtr.Zero;
        IntPtr memoryDc = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;
        IntPtr previous = IntPtr.Zero;

        try
        {
            var width = GetSystemMetrics(SM_CXSCREEN);
            var height = GetSystemMetrics(SM_CYSCREEN);

            if (width <= 0 || height <= 0)
            {
                return Result<ScreenshotResult>.Failure("I couldn't work out your screen size.");
            }

            screenDc = GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero)
            {
                return Result<ScreenshotResult>.Failure("I couldn't access the screen.");
            }

            memoryDc = CreateCompatibleDC(screenDc);
            bitmap = CreateCompatibleBitmap(screenDc, width, height);
            if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero)
            {
                return Result<ScreenshotResult>.Failure("I couldn't prepare the capture.");
            }

            previous = SelectObject(memoryDc, bitmap);
            if (!BitBlt(memoryDc, 0, 0, width, height, screenDc, 0, 0, SourceCopy))
            {
                return Result<ScreenshotResult>.Failure("I couldn't copy the screen.");
            }

            var stride = width * BitsPerPixel / 8;
            pixels = new byte[stride * height];

            var header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,
                Height = -height, // Negative height requests a top-down image.
                Planes = 1,
                BitCount = BitsPerPixel,
                Compression = BiRgb
            };

            if (!GetDIBits(
                    memoryDc,
                    bitmap,
                    0,
                    (uint)height,
                    pixels,
                    ref header,
                    0))
            {
                return Result<ScreenshotResult>.Failure("I couldn't read the captured image.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            var png = await EncodePngAsync(pixels, width, height).ConfigureAwait(false);
            var folder = await ResolveFolderAsync(cancellationToken).ConfigureAwait(false);

            var file = await SaveAsync(folder, png, cancellationToken).ConfigureAwait(false);

            return Result<ScreenshotResult>.Success(new ScreenshotResult(file.Path, (long)file.Size, DateTimeOffset.UtcNow));
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Screen capture was cancelled.");
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Screen capture failed.");
            return Result<ScreenshotResult>.Failure("I couldn't take a screenshot.");
        }
        finally
        {
            if (memoryDc != IntPtr.Zero && previous != IntPtr.Zero)
            {
                SelectObject(memoryDc, previous);
            }

            if (bitmap != IntPtr.Zero)
            {
                DeleteObject(bitmap);
            }

            if (memoryDc != IntPtr.Zero)
            {
                DeleteDC(memoryDc);
            }

            if (screenDc != IntPtr.Zero)
            {
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }

    private async Task<string> ResolveFolderAsync(CancellationToken cancellationToken)
    {
        var configured = _options.CurrentValue.ScreenshotFolder;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            Directory.CreateDirectory(configured);
            return configured;
        }

        var pictures = await _knownFolders
            .GetPathAsync(KnownFolderKind.Pictures, cancellationToken)
            .ConfigureAwait(false);

        if (pictures.IsFailure || string.IsNullOrWhiteSpace(pictures.Value))
        {
            // The shell lookup failed, so the capture is not written anywhere rather than being
            // dropped somewhere the user would not think to look.
            throw new InvalidOperationException("The Pictures folder could not be located.");
        }

        Directory.CreateDirectory(pictures.Value);
        return pictures.Value;
    }

    private static async Task<byte[]> EncodePngAsync(byte[] pixels, int width, int height)
    {
        using var stream = new InMemoryRandomAccessStream();

        // The encoder writes into the stream it is created with, so the stream is supplied up
        // front rather than attached afterwards.
        var encoder = await BitmapEncoder
            .CreateAsync(BitmapEncoder.PngEncoderId, stream)
            .AsTask()
            .ConfigureAwait(false);

        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            (uint)width,
            (uint)height,
            96,
            96,
            pixels);

        await encoder.FlushAsync().AsTask().ConfigureAwait(false);

        if (stream.Size == 0)
        {
            throw new InvalidOperationException("The imaging pipeline produced no image data.");
        }

        stream.Seek(0);
        var reader = new DataReader(stream.GetInputStreamAt(0));
        try
        {
            var loaded = await reader.LoadAsync((uint)stream.Size).AsTask().ConfigureAwait(false);

            var encoded = new byte[loaded];
            reader.ReadBytes(encoded);

            if (encoded.Length == 0 || encoded[0] != PngSignature)
            {
                throw new InvalidOperationException("The imaging pipeline did not return a PNG image.");
            }

            return encoded;
        }
        finally
        {
            reader.Dispose();
        }
    }

    private static async Task<(string Path, ulong Size)> SaveAsync(
        string folder,
        byte[] contents,
        CancellationToken cancellationToken)
    {
        var name = $"Screenshot {DateTime.Now:yyyy-MM-dd HH-mm-ss}.png";
        var path = Path.Combine(folder, name);

        await File.WriteAllBytesAsync(path, contents, cancellationToken).ConfigureAwait(false);

        var info = new FileInfo(path);

        return (info.FullName, (ulong)info.Length);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr GetDC(IntPtr window);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int ReleaseDC(IntPtr window, IntPtr deviceContext);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial IntPtr CreateCompatibleBitmap(IntPtr deviceContext, int width, int height);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial IntPtr SelectObject(IntPtr deviceContext, IntPtr handle);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(
        IntPtr destination,
        int destinationX,
        int destinationY,
        int width,
        int height,
        IntPtr source,
        int sourceX,
        int sourceY,
        uint rasterOperation);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr handle);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr deviceContext);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDIBits(
        IntPtr deviceContext,
        IntPtr bitmap,
        uint startScan,
        uint scanLines,
        byte[] bits,
        ref BitmapInfoHeader header,
        uint usage);
}
