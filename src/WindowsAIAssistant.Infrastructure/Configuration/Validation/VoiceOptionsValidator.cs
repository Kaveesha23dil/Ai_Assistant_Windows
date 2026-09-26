using Microsoft.Extensions.Options;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.Configuration.Validation;

/// <summary>
/// Rejects a voice configuration that could not be honoured.
/// <para>
/// Range checks live here rather than being silently clamped, because a rate of 4.0 or a
/// confidence threshold of 50 in a configuration file is a mistake the user needs to hear
/// about at startup instead of a value that quietly behaves differently from what they wrote.
/// </para>
/// </summary>
public sealed class VoiceOptionsValidator : IValidateOptions<VoiceOptions>
{
    private const double MinimumSpeechRate = 0.5;
    private const double MaximumSpeechRate = 2.0;
    private const int MaximumSpokenLength = 4000;

    public ValidateOptionsResult Validate(string? name, VoiceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Language))
        {
            failures.Add("Voice:Language must name a language, for example \"en-US\".");
        }

        if (options.SpeechRate is < MinimumSpeechRate or > MaximumSpeechRate)
        {
            failures.Add(
                $"Voice:SpeechRate must be between {MinimumSpeechRate} and {MaximumSpeechRate}.");
        }

        if (options.SpeechPitch is < MinimumSpeechRate or > MaximumSpeechRate)
        {
            failures.Add(
                $"Voice:SpeechPitch must be between {MinimumSpeechRate} and {MaximumSpeechRate}.");
        }

        if (options.SpeechVolume is < 0.0 or > 1.0)
        {
            failures.Add("Voice:SpeechVolume must be between 0.0 and 1.0.");
        }

        if (options.MinimumCommandConfidence is < 0.0 or > 1.0)
        {
            failures.Add("Voice:MinimumCommandConfidence must be between 0.0 and 1.0.");
        }

        if (options.MaximumSpokenResponseLength <= 0)
        {
            failures.Add("Voice:MaximumSpokenResponseLength must be greater than zero.");
        }
        else if (options.MaximumSpokenResponseLength > MaximumSpokenLength)
        {
            failures.Add(
                $"Voice:MaximumSpokenResponseLength must not exceed {MaximumSpokenLength} characters.");
        }

        if (options.SilenceTimeoutMilliseconds < 200)
        {
            failures.Add("Voice:SilenceTimeoutMilliseconds must be at least 200.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
