using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Vision;

/// <summary>
/// What this particular machine can actually do with a screen.
/// <para>
/// Asked once and shown to the person, because the useful thing to say is not "unsupported
/// exception" but "this computer has no text recognition, so describe the screen will still
/// work but reading the text will not". Each entry is a fact about the device rather than a
/// guess from a version number.
/// </para>
/// </summary>
public sealed record VisionCapabilities
{
    public static VisionCapabilities Unavailable { get; } = new(
        captureSupported: false,
        ocrProvider: OcrProviderKind.None,
        ocrAvailable: false,
        modelSupportsImageInput: false,
        cloudConfigured: false,
        cloudVisionAvailable: false,
        imageSizeLimitBytes: 0,
        maxDimension: 0,
        source: "unknown");

    public VisionCapabilities(
        bool captureSupported,
        OcrProviderKind ocrProvider,
        bool ocrAvailable,
        bool modelSupportsImageInput,
        bool cloudConfigured,
        bool cloudVisionAvailable,
        int imageSizeLimitBytes,
        int maxDimension,
        string? source)
    {
        CaptureSupported = captureSupported;
        OcrProvider = ocrProvider;
        OcrAvailable = ocrAvailable;
        ModelSupportsImageInput = modelSupportsImageInput;
        CloudConfigured = cloudConfigured;
        CloudVisionAvailable = cloudVisionAvailable;
        ImageSizeLimitBytes = imageSizeLimitBytes;
        MaxDimension = maxDimension;
        Source = string.IsNullOrWhiteSpace(source) ? null : source;
    }

    /// <summary>Gets a value indicating whether the Windows capture path is usable here.</summary>
    public bool CaptureSupported { get; }

    /// <summary>Gets which text engine would be used, or <see cref="OcrProviderKind.None"/>.</summary>
    public OcrProviderKind OcrProvider { get; }

    /// <summary>Gets a value indicating whether any text engine is usable.</summary>
    public bool OcrAvailable { get; }

    /// <summary>Gets a value indicating whether the configured model accepts an image at all.</summary>
    public bool ModelSupportsImageInput { get; }

    /// <summary>Gets a value indicating whether a cloud provider has credentials configured.</summary>
    public bool CloudConfigured { get; }

    /// <summary>Gets a value indicating whether a cloud provider is both configured and able to see images.</summary>
    public bool CloudVisionAvailable { get; }

    /// <summary>Gets the largest image that will be sent, in bytes. Zero means no limit is applied.</summary>
    public int ImageSizeLimitBytes { get; }

    /// <summary>Gets the longest edge an image will be scaled to. Zero means images are not scaled.</summary>
    public int MaxDimension { get; }

    /// <summary>Gets where these answers came from, for the status line.</summary>
    public string? Source { get; }

    /// <summary>Gets a value indicating whether a screenshot can be taken and shown at all.</summary>
    public bool CanAnalyzeVisually => CaptureSupported && ModelSupportsImageInput;

    /// <summary>Gets a value indicating whether text can be read on this machine with no network.</summary>
    public bool CanReadTextLocally => OcrAvailable;
}
