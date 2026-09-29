namespace WindowsAIAssistant.Application.Vision;

/// <summary>
/// The fixed limits the visual path works within.
/// <para>
/// A class of constants rather than configuration, on purpose for the ones that are about
/// correctness and not taste. The character budget on recognized text in particular is a
/// correctness limit: a screenshot's worth of OCR text is unbounded in a way a passage of a
/// document is not, and a prompt that grows without bound fails at the provider rather than
/// here, where the person could be told.
/// </para>
/// </summary>
public static class ScreenVisionLimits
{
    /// <summary>
    /// How much recognized text to include in a prompt. Large enough to carry a dialog or an
    /// error, small enough that the instructions around it still fit.
    /// </summary>
    public const int OcrPromptCharacterBudget = 4_000;

    /// <summary>
    /// The longest edge a screenshot is scaled to before being sent.
    /// <para>
    /// Well above a normal window capture and above a 1080p display, so ordinary use is never
    /// affected. It exists for the 4K and 5K monitors where an unscaled frame is both too large
    /// for a model and mostly empty space.
    /// </para>
    /// </summary>
    public const int MaxImageDimension = 2_560;

    /// <summary>
    /// The largest encoded image that will be sent. A PNG screenshot of a mostly-white 4K
    /// display can exceed this while its dimensions look reasonable, so the limit is enforced on
    /// the encoded size and the image is re-encoded as JPEG when it is still over.
    /// </summary>
    public const int MaxImageBytes = 4 * 1024 * 1024;

    /// <summary>
    /// How long a capture may take before it is treated as failed rather than pending forever.
    /// <para>
    /// The capture picker is a person choosing a target, so this has to be generous. It exists
    /// for the case where the picker never returns, which would otherwise leave the overlay
    /// stuck in a waiting state for the rest of the session.
    /// </para>
    /// </summary>
    public static readonly TimeSpan CaptureTimeout = TimeSpan.FromMinutes(2);

    /// <summary>How long local text recognition may take before the image is analyzed anyway.</summary>
    public static readonly TimeSpan OcrTimeout = TimeSpan.FromSeconds(12);
}
