using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Vision;

/// <summary>
/// One captured frame, in memory, together with what is known about where it came from.
/// <para>
/// The image bytes live here and nowhere else. Nothing writes a capture to disk unless a
/// person asks for a file, nothing keeps one alive between requests, and disposing this object
/// is the documented way to erase it: the buffer is overwritten rather than merely dropped, so a
/// screenshot of a bank statement does not stay readable in freed memory for the rest of the
/// session because a reference happened to be held by an event handler.
/// </para>
/// </summary>
public sealed class ScreenCaptureResult : IDisposable
{
    private byte[]? _imageBytes;

    public ScreenCaptureResult(
        byte[] imageBytes,
        int width,
        int height,
        ScreenPixelFormat pixelFormat,
        ScreenCaptureType captureType,
        DateTimeOffset capturedAt,
        string? sourceDisplayName = null,
        ScreenRegion? region = null)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (imageBytes.Length == 0)
        {
            throw new ArgumentException("A captured frame must contain at least one byte.", nameof(imageBytes));
        }

        _imageBytes = imageBytes;
        Width = width;
        Height = height;
        PixelFormat = pixelFormat;
        CaptureType = captureType;
        CapturedAt = capturedAt;
        SourceDisplayName = string.IsNullOrWhiteSpace(sourceDisplayName) ? null : sourceDisplayName;
        Region = region;
    }

    /// <summary>Gets the encoded image. Throws once disposed.</summary>
    public ReadOnlyMemory<byte> ImageBytes
    {
        get
        {
            var bytes = _imageBytes ?? throw new ObjectDisposedException(nameof(ScreenCaptureResult));
            return bytes;
        }
    }

    /// <summary>Gets the width of the frame in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height of the frame in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the layout of the frame's pixels.</summary>
    public ScreenPixelFormat PixelFormat { get; }

    /// <summary>Gets what was captured.</summary>
    public ScreenCaptureType CaptureType { get; }

    /// <summary>Gets when the frame was taken.</summary>
    public DateTimeOffset CapturedAt { get; }

    /// <summary>
    /// Gets the name of the display or window the frame came from, when Windows supplied one.
    /// <para>
    /// A display name can contain a document title, so it is kept out of prompts and out of logs
    /// and is only ever shown to the person who was already looking at that display.
    /// </para>
    /// </summary>
    public string? SourceDisplayName { get; }

    /// <summary>Gets the region that was cropped, when the frame is the result of a selection.</summary>
    public ScreenRegion? Region { get; }

    /// <summary>Gets a value indicating whether the buffer has been erased.</summary>
    public bool IsReleased => _imageBytes is null;

    /// <summary>
    /// Returns a description of this frame that can be sent to a model: its size, what was
    /// captured, and, when it matters to the answer, that only part of a screen was included.
    /// <para>
    /// Exported by a method rather than by letting a caller build the sentence, because a
    /// caption that has to be written at the call site is a caption that will eventually be
    /// written to include a window title.
    /// </para>
    /// </summary>
    public string BuildCaption() => CaptureType switch
    {
        ScreenCaptureType.SelectedRegion =>
            $"A {Width}x{Height} pixel region that someone selected on a larger screen, and nothing outside it.",
        ScreenCaptureType.Window =>
            $"A {Width}x{Height} pixel screenshot of a single application window that someone chose.",
        ScreenCaptureType.Display =>
            $"A {Width}x{Height} pixel screenshot of one display that someone chose.",
        _ => $"A {Width}x{Height} pixel screenshot that someone chose."
    };

    /// <inheritdoc />
    public void Dispose()
    {
        if (_imageBytes is null)
        {
            return;
        }

        Array.Clear(_imageBytes);
        _imageBytes = null;
    }
}
