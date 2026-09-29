namespace WindowsAIAssistant.Infrastructure.Configuration.Options;

/// <summary>
/// The visual-assistant configuration, bound from the <c>Vision</c> section.
/// <para>
/// Nothing here is a permission. Whether an image may be taken, read, sent, or written to disk
/// is decided by <see cref="PrivacyOptions"/>, and keeping the two apart is deliberate: these
/// are limits and choices about how the work is done, and a settings page that let a size
/// slider imply permission to look at the screen would be a page that can be misread.
/// </para>
/// <para>
/// Every default is the smaller or slower choice. A 2560-pixel ceiling on the longest edge is
/// the point below which a screenshot of a normal display stays readable after scaling, and the
/// four-megabyte ceiling is well inside what an image-capable model will accept, so neither
/// one has to be raised to make ordinary use work.
/// </para>
/// <para>
/// There is no lossy-compression setting, and that is not an oversight. An over-budget image is
/// made smaller by being scaled rather than by being re-encoded at lower quality, because the
/// image is a screenshot: the only reason anybody took it is the small text in it, and
/// compression artefacts land hardest on exactly the glyphs that were worth capturing. It also
/// would not be honest to expose the knob, since the Windows imaging encoder in this platform
/// offers no way to set it.
/// </para>
/// </summary>
public sealed class VisionOptions
{
    public const string SectionName = "Vision";

    /// <summary>
    /// Gets the longest edge an image is scaled down to before being sent or read.
    /// <para>
    /// Applied to the copy that goes to a model, never to the copy the person sees in the
    /// preview. A preview that quietly became half-size would be a lie about their own screen,
    /// and the whole point of the preview is that it is what they chose.
    /// </para>
    /// </summary>
    public int MaxImageDimension { get; init; } = 2560;

    /// <summary>
    /// Gets the ceiling on an encoded image, in bytes. Zero disables the check.
    /// <para>
    /// A limit rather than a target: a screenshot of a 4K display encodes to somewhere north of
    /// eight megabytes, and no model wants one. The image is scaled until it fits rather than
    /// being refused, because a person asking what is on their screen did not ask to be told
    /// their screen is too big.
    /// </para>
    /// </summary>
    public int MaxImageBytes { get; init; } = 4 * 1024 * 1024;

    /// <summary>
    /// Gets how many times an over-budget image is scaled down before it is refused.
    /// <para>
    /// Each pass reduces the longest edge by a quarter, so five passes take a 3840-pixel
    /// display to 240. Bounded so a hopeless image produces a clear refusal rather than a long
    /// stall, and so nothing can shrink a screenshot down to a thumbnail without somebody
    /// noticing.
    /// </para>
    /// </summary>
    public int MaxScaleAttempts { get; init; } = 5;

    /// <summary>
    /// Gets which text engine to prefer: <c>Auto</c>, <c>WindowsAi</c>, or <c>WindowsLegacy</c>.
    /// <para>
    /// Defaults to <c>Auto</c>, which takes the best engine this machine can actually run. A
    /// named engine that turns out to be unavailable falls back rather than failing, because
    /// "read my screen" is worth doing with an older recogniser and is not worth failing over.
    /// </para>
    /// </summary>
    public string PreferredOcrProvider { get; init; } = "Auto";

    /// <summary>
    /// Gets a value indicating whether the application may download a text recognition model the
    /// first time one is needed. Off by default.
    /// <para>
    /// Consent to read the screen is not consent to fetch a hundred megabytes from the store,
    /// and on a metered connection the difference is visible on the bill. With this off, the
    /// newer engine is used only if it is already present.
    /// </para>
    /// </summary>
    public bool AllowOcrModelDownload { get; init; }

    /// <summary>
    /// Gets the model identifier sent to a cloud provider for visual analysis. A text-only
    /// model is a supported configuration: the provider reports that it cannot see, and the
    /// status line says so rather than the request failing.
    /// </summary>
    public string VisionModel { get; init; } = "gpt-4o";

    /// <summary>
    /// Gets the folder screenshots are written to when none is named, or
    /// <see langword="null"/> for the visible Pictures location.
    /// <para>
    /// Never a hidden application folder. A screenshot a person cannot find is not something
    /// they can delete when they change their mind.
    /// </para>
    /// </summary>
    public string? ScreenshotFolder { get; init; }

    /// <summary>
    /// Gets how long to wait for the Windows capture picker before giving up, in seconds.
    /// <para>
    /// Long enough that somebody can find the window they meant, and bounded so a picker that
    /// never returns does not hold a request open for the rest of the session.
    /// </para>
    /// </summary>
    public int CaptureTimeoutSeconds { get; init; } = 120;
}
