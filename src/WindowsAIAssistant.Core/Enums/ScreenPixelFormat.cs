namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// The layout of a captured frame's pixels.
/// <para>
/// Named rather than left as a format constant so that a provider which only accepts one
/// order says so in a type, and so that a capture and a crop cannot disagree about the
/// arrangement of their own bytes.
/// </para>
/// </summary>
public enum ScreenPixelFormat
{
    /// <summary>Unknown, or not set. Treated as four-channel when a byte count must be assumed.</summary>
    Unknown = 0,

    /// <summary>Four bytes per pixel, blue first.</summary>
    Bgra8 = 1,

    /// <summary>Four bytes per pixel, red first.</summary>
    Rgba8 = 2
}
