namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// A qualification on an answer that did not stop it being produced.
/// <para>
/// Modelled the way document warnings are, and for the same reason: a screenshot read at a
/// third of a scale factor is still a screenshot worth reading, and reporting that honestly is
/// more useful than either hiding the limitation or throwing the work away.
/// </para>
/// </summary>
public enum ScreenWarningKind
{
    /// <summary>Optical character recognition ran but recognised little or no text.</summary>
    OcrTextUnclear = 0,

    /// <summary>
    /// The image contained something that looks like a password, token, or card number, and the
    /// answer was kept away from it.
    /// </summary>
    SensitiveContentSkipped = 1,

    /// <summary>The text recovered from the image is a reading, not a copy, and may differ in detail.</summary>
    OcrReadingApproximate = 2,

    /// <summary>The frame was captured at a reduced size, so small text may be missing from it.</summary>
    ImageResized = 3,

    /// <summary>
    /// The frame was blank or black, which usually means the content is protected and Windows
    /// declined to hand it over.
    /// </summary>
    ProtectedContentNotCaptured = 4,

    /// <summary>
    /// The frame was analysed without any text, because recognition was unavailable or was
    /// declined.
    /// </summary>
    TextNotRecognized = 5,

    /// <summary>
    /// The frame was answered from local text recovery alone, because no vision model was
    /// configured to look at it.
    /// <para>
    /// Distinct from a failure because the answer may still be worth reading — it just describes
    /// what was written rather than what was shown, and a person who wanted the picture
    /// described needs to know that they did not get it.
    /// </para>
    /// </summary>
    ModelNotConfigured = 6,

    /// <summary>
    /// The image was sent to a cloud provider because cloud analysis was permitted.
    /// <para>
    /// A warning rather than a detail because it is the one thing in this feature that cannot be
    /// undone afterwards, and a person reading a transcript of the request should be able to see
    /// that it happened without trusting that they remember turning a switch on.
    /// </para>
    /// </summary>
    SentToCloudProvider = 7
}
