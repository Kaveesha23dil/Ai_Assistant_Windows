using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Vision;

/// <summary>
/// One request to look at an image and say something about it.
/// <para>
/// Carries the image, the task, and the permission, and deliberately not much else. There is no
/// screen contents, no window list, no running applications, no file paths, and no conversation:
/// a provider is handed this and told what it needs, so that a person who takes a screenshot of
/// one dialog cannot end up with their whole desktop attached to a question about that dialog.
/// </para>
/// </summary>
public sealed record ScreenAnalysisRequest
{
    public ScreenAnalysisRequest(
        ReadOnlyMemory<byte> imageBytes,
        ScreenAnalysisType analysisType,
        VisionPrivacyContext privacy,
        string? userQuestion = null,
        OcrResult? ocr = null,
        int width = 0,
        int height = 0,
        ScreenCaptureType captureType = ScreenCaptureType.None,
        ScreenRegion? region = null)
    {
        if (imageBytes.IsEmpty)
        {
            throw new ArgumentException("An analysis request has to carry an image.", nameof(imageBytes));
        }

        ArgumentNullException.ThrowIfNull(privacy);

        ImageBytes = imageBytes;
        AnalysisType = analysisType;
        Privacy = privacy;
        UserQuestion = string.IsNullOrWhiteSpace(userQuestion) ? null : userQuestion;
        Ocr = ocr is { HasText: true } ? ocr : null;
        Width = width;
        Height = height;
        CaptureType = captureType;
        Region = region;
    }

    /// <summary>Gets the encoded image to look at.</summary>
    public ReadOnlyMemory<byte> ImageBytes { get; }

    /// <summary>Gets what the person is asking for.</summary>
    public ScreenAnalysisType AnalysisType { get; }

    /// <summary>Gets the permission in force for this request.</summary>
    public VisionPrivacyContext Privacy { get; }

    /// <summary>Gets the person's own words, when they asked a question rather than a task.</summary>
    public string? UserQuestion { get; }

    /// <summary>
    /// Gets text already recovered from the image on this machine, if any.
    /// <para>
    /// Passed to the model as a reading of the image rather than as a separate document, so that
    /// small text the model cannot resolve is still available to it without the picture having
    /// to be legible enough to read directly.
    /// </para>
    /// </summary>
    public OcrResult? Ocr { get; }

    /// <summary>Gets the image width in pixels, or zero when the provider need not know.</summary>
    public int Width { get; }

    /// <summary>Gets the image height in pixels, or zero when the provider need not know.</summary>
    public int Height { get; }

    /// <summary>Gets what was captured, so a provider can avoid claiming more than it saw.</summary>
    public ScreenCaptureType CaptureType { get; }

    /// <summary>Gets the selected region, when the image is part of a larger screen.</summary>
    public ScreenRegion? Region { get; }
}
