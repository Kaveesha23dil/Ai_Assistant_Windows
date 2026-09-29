namespace WindowsAIAssistant.Core.Models.Vision;

/// <summary>
/// One piece of text found in an image, and where it was found.
/// <summary>
public sealed record OcrTextRegion
{
    public OcrTextRegion(string text, ScreenRegion boundingBox, double confidence = double.NaN)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(boundingBox);

        Text = text;
        BoundingBox = boundingBox;
        Confidence = confidence;
    }

    /// <summary>Gets the text as recognised.</summary>
    public string Text { get; }

    /// <summary>Gets where the text sits in the image it was read from.</summary>
    public ScreenRegion BoundingBox { get; }

    /// <summary>
    /// Gets the recogniser's confidence, or <see cref="double.NaN"/> when the engine does not
    /// report one. The legacy engine does not, and treating its silence as zero would be a lie
    /// that reads as a very low score.
    /// </summary>
    public double Confidence { get; }

    /// <summary>Gets a value indicating whether a usable confidence score was reported.</summary>
    public bool HasConfidence => !double.IsNaN(Confidence);
}
