namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// What a screen capture was taken of.
/// <para>
/// The value is carried on the result and into the prompt, because it changes what the
/// assistant is allowed to claim. A description of one window is not a description of the
/// desktop, and an answer that cannot tell the difference is one the person cannot rely on.
/// </para>
/// </summary>
public enum ScreenCaptureType
{
    /// <summary>Nothing was captured. Used when a request named no target.</summary>
    None = 0,

    /// <summary>A whole display, chosen by the person in the Windows capture picker.</summary>
    Display = 1,

    /// <summary>
    /// One application window, chosen by the person in the Windows capture picker.
    /// </summary>
    Window = 2,

    /// <summary>
    /// A rectangle the person dragged on top of a capture they had already chosen.
    /// <para>
    /// Its own value rather than a flag on a display capture, because the rectangle is the
    /// privacy boundary: nothing outside it is read, resized, or sent, and saying so later
    /// requires having recorded it here.
    /// </para>
    /// </summary>
    SelectedRegion = 3
}
