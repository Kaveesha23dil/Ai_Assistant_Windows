using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Vision;

/// <summary>
/// What someone asked to be captured.
/// <para>
/// No target is named, and no coordinate is carried, because there is no way to ask for a
/// window by title or for a display by index from Application or Core: the only thing that can
/// know what is on screen is the platform, and the platform is asked through the Windows
/// capture picker, on this machine, with this person watching.
/// </para>
/// </summary>
public sealed record ScreenCaptureRequest
{
    public ScreenCaptureRequest(
        ScreenCaptureType captureType,
        ScreenRegion? region = null,
        string? suggestedFileName = null)
    {
        if (captureType == ScreenCaptureType.SelectedRegion && region is null)
        {
            throw new ArgumentException(
                "A region capture has to name the rectangle it means.",
                nameof(region));
        }

        CaptureType = captureType;
        Region = region;
        SuggestedFileName = string.IsNullOrWhiteSpace(suggestedFileName) ? null : suggestedFileName;
    }

    /// <summary>Gets what should be captured.</summary>
    public ScreenCaptureType CaptureType { get; }

    /// <summary>
    /// Gets the rectangle to crop, in the coordinates of a frame already captured.
    /// <para>
    /// Only meaningful for <see cref="ScreenCaptureType.SelectedRegion"/>, and applied to a
    /// frame rather than to the desktop, for the reasons on <see cref="ScreenRegion"/>.
    /// </para>
    /// </summary>
    public ScreenRegion? Region { get; }

    /// <summary>Gets a name to offer if the person later asks for the frame to be saved.</summary>
    public string? SuggestedFileName { get; }

    /// <summary>Creates a request for one display, chosen by the person.</summary>
    public static ScreenCaptureRequest ForDisplay() => new(ScreenCaptureType.Display);

    /// <summary>Creates a request for one window, chosen by the person.</summary>
    public static ScreenCaptureRequest ForWindow() => new(ScreenCaptureType.Window);

    /// <summary>Creates a request for a rectangle of an already-captured frame.</summary>
    public static ScreenCaptureRequest ForRegion(ScreenRegion region) =>
        new(ScreenCaptureType.SelectedRegion, region);
}
