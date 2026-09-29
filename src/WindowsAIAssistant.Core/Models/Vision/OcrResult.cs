using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Vision;

/// <summary>
/// The text read out of an image on this machine.
/// <para>
/// Nothing in this type has ever been near a network. It is the answer to "what does it say"
/// even with every cloud switch turned off, and it is the part of a visual request that can be
/// sent to a model safely, because a model can be asked about words without being handed the
/// picture they were read from.
/// </para>
/// </summary>
public sealed record OcrResult
{
    public static readonly OcrResult Empty = new(
        string.Empty,
        OcrProviderKind.None,
        null,
        double.NaN,
        Array.Empty<OcrTextRegion>());

    public OcrResult(
        string text,
        OcrProviderKind provider,
        string? language,
        double confidence,
        IReadOnlyCollection<OcrTextRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(regions);

        Text = text;
        Provider = provider;
        Language = string.IsNullOrWhiteSpace(language) ? null : language;
        Confidence = confidence;
        Regions = regions;
    }

    /// <summary>Gets the recovered text, or the empty string when nothing was recognised.</summary>
    public string Text { get; }

    /// <summary>Gets which engine read it, or <see cref="OcrProviderKind.None"/>.</summary>
    public OcrProviderKind Provider { get; }

    /// <summary>Gets the recognised language tag, when the engine reported one.</summary>
    public string? Language { get; }

    /// <summary>Gets the mean confidence, or <see cref="double.NaN"/> when none was reported.</summary>
    public double Confidence { get; }

    /// <summary>Gets the individual text boxes, in reading order where the engine gave one.</summary>
    public IReadOnlyCollection<OcrTextRegion> Regions { get; }

    /// <summary>Gets a value indicating whether anything was read.</summary>
    public bool HasText => !string.IsNullOrWhiteSpace(Text);

    /// <summary>Gets a value indicating whether an engine reported any confidence at all.</summary>
    public bool HasConfidence => !double.IsNaN(Confidence);
}
