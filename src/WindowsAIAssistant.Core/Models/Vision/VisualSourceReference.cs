using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Vision;

/// <summary>
/// What is on the assistant's screen right now, without the screen itself.
/// <para>
/// A description of where a frame came from: which kind of target, which display, what size,
/// whether it was a region of something larger, and when. It carries no pixels and no text, so
/// it is safe to put on a chip, in a status line, or in a log, and it is enough for the person
/// to see what they are about to talk about.
/// </para>
/// </summary>
public sealed record VisualSourceReference
{
    public VisualSourceReference(
        ScreenCaptureType captureType,
        int width,
        int height,
        DateTimeOffset capturedAt,
        string? sourceDisplayName = null,
        ScreenRegion? region = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);

        CaptureType = captureType;
        Width = width;
        Height = height;
        CapturedAt = capturedAt;
        SourceDisplayName = string.IsNullOrWhiteSpace(sourceDisplayName) ? null : sourceDisplayName;
        Region = region;
    }

    /// <summary>Gets what was captured.</summary>
    public ScreenCaptureType CaptureType { get; }

    /// <summary>Gets the width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets when the frame was taken.</summary>
    public DateTimeOffset CapturedAt { get; }

    /// <summary>Gets the display or window name, when one was available.</summary>
    public string? SourceDisplayName { get; }

    /// <summary>Gets the selected region, when the frame is part of a larger screen.</summary>
    public ScreenRegion? Region { get; }

    /// <summary>Gets a short description for a chip or a status line.</summary>
    public string BuildLabel()
    {
        var what = CaptureType switch
        {
            ScreenCaptureType.SelectedRegion => "Screen region",
            ScreenCaptureType.Window => "App window",
            ScreenCaptureType.Display => "Display",
            _ => "Screen"
        };

        return $"{what} · {Width}×{Height}";
    }

    /// <summary>Creates a reference from a capture, without retaining it.</summary>
    public static VisualSourceReference FromCapture(ScreenCaptureResult capture)
    {
        ArgumentNullException.ThrowIfNull(capture);

        return new VisualSourceReference(
            capture.CaptureType,
            capture.Width,
            capture.Height,
            capture.CapturedAt,
            capture.SourceDisplayName,
            capture.Region);
    }
}
