using Microsoft.Extensions.Options;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.Configuration.Validation;

/// <summary>
/// Checks that the visual-assistant limits describe work somebody could actually finish.
/// <para>
/// These are bounds rather than preferences, so each is checked as one: a dimension of zero
/// would mean "scale to nothing", a byte ceiling of one would mean every screenshot is refused,
/// and a scale budget of zero would mean a 4K display can never be made small enough.
/// </para>
/// <para>
/// The provider name is checked for emptiness only, not against a list. An unrecognised engine
/// is a reported setting rather than a refused start, and the resolver falls back to whatever
/// is available instead of taking the feature down with it.
/// </para>
/// </summary>
public sealed class VisionOptionsValidator : IValidateOptions<VisionOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, VisionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.MaxImageDimension is < 320 or > 8192)
        {
            failures.Add(
                "Vision:MaxImageDimension must be between 320 and 8192. Anything below 320 "
                + "cannot hold a legible screenshot, and anything above 8192 is larger than any "
                + "display this can capture.");
        }

        if (options.MaxImageBytes is < 0 or > 32 * 1024 * 1024)
        {
            failures.Add(
                "Vision:MaxImageBytes must be between 0 (no limit) and 33554432. An image-capable "
                + "model will not accept more, so a larger ceiling only defers the failure.");
        }

        if (options.MaxScaleAttempts is < 1 or > 10)
        {
            failures.Add("Vision:MaxScaleAttempts must be between 1 and 10.");
        }

        if (string.IsNullOrWhiteSpace(options.PreferredOcrProvider))
        {
            failures.Add("Vision:PreferredOcrProvider must name an engine.");
        }

        if (string.IsNullOrWhiteSpace(options.VisionModel))
        {
            failures.Add("Vision:VisionModel must name a model.");
        }

        if (options.CaptureTimeoutSeconds is < 5 or > 900)
        {
            failures.Add(
                "Vision:CaptureTimeoutSeconds must be between 5 and 900. Below five seconds is "
                + "not long enough to choose a window; above fifteen minutes is a request left "
                + "open long after the person stopped looking at it.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Gets the engine names this build knows about, for the resolver and for a message that
    /// explains a mistyped setting.
    /// </summary>
    public static IReadOnlyList<string> KnownOcrProviders { get; } =
        new[] { "Auto", "WindowsAi", "WindowsLegacy" };
}
