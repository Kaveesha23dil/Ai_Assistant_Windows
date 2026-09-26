namespace WindowsAIAssistant.Core.Models.Voice;

/// <summary>
/// Provider-independent tuning for spoken replies. Every value is clamped on construction so
/// a configuration mistake can never push a voice or rate outside what a synthesizer accepts.
/// </summary>
public sealed record SpeechSynthesisOptions
{
    private const double MinimumRate = 0.5;
    private const double MaximumRate = 2.0;
    private const double MinimumVolume = 0.0;
    private const double MaximumVolume = 1.0;

    /// <summary>
    /// Gets the requested voice name. When null the implementation selects a voice for
    /// <see cref="Language"/>.
    /// </summary>
    public string? VoiceName { get; init; }

    /// <summary>Gets the requested BCP-47 language tag.</summary>
    public string Language { get; init; } = "en-US";

    /// <summary>Gets the speaking rate, clamped to the range 0.5 to 2.0.</summary>
    public double Rate { get; init; } = 1.0;

    /// <summary>Gets the pitch, clamped to the range 0.5 to 2.0.</summary>
    public double Pitch { get; init; } = 1.0;

    /// <summary>Gets the output volume, clamped to the range 0.0 to 1.0.</summary>
    public double Volume { get; init; } = 1.0;

    /// <summary>
    /// Gets the maximum number of characters to speak. Longer responses are shortened before
    /// they reach the synthesizer, because reading a full AI answer aloud is unhelpful.
    /// </summary>
    public int MaximumSpeechLength { get; init; } = 240;

    /// <summary>Clamps every value into its supported range.</summary>
    public SpeechSynthesisOptions Normalized() => this with
    {
        Language = string.IsNullOrWhiteSpace(Language) ? "en-US" : Language.Trim(),
        VoiceName = string.IsNullOrWhiteSpace(VoiceName) ? null : VoiceName.Trim(),
        Rate = Math.Clamp(Rate, MinimumRate, MaximumRate),
        Pitch = Math.Clamp(Pitch, MinimumRate, MaximumRate),
        Volume = Math.Clamp(Volume, MinimumVolume, MaximumVolume),
        MaximumSpeechLength = Math.Max(0, MaximumSpeechLength)
    };
}
