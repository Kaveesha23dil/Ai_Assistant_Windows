using System.Text.RegularExpressions;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Vision;

namespace WindowsAIAssistant.Application.Vision;

/// <summary>
/// Finds things in recognized text that should not be read, repeated, or sent anywhere.
/// <para>
/// This runs before a screenshot's text is put in front of a model, and the point is narrow: a
/// person asking "what's this error" while their password manager is visible has handed over a
/// frame that contains their master password, and a model that faithfully transcribes the
/// screen will helpfully print it. The consent switch decides whether the picture may be sent;
/// this decides whether a secret inside it goes along.
/// </para>
/// <para>
/// Redaction is by substitution rather than omission, and the placeholder says what was
/// withheld. A model told that a field is present but has been blanked will say "there is a
/// password field", which is the useful answer; told that text was removed it may guess.
/// </para>
/// <para>
/// The patterns are heuristic and therefore imperfect, and that is a deliberate trade. A missed
/// match leaks; a false positive costs a redaction of something harmless. Weighted for that,
/// these err towards redacting, and nothing here is described to a person as a guarantee.
/// </para>
/// </summary>
public static partial class ScreenSafetyFilter
{
    /// <summary>What a redacted run of text is replaced with.</summary>
    public const string RedactionMarker = "[redacted]";

    /// <summary>The result of screening some text.</summary>
    public sealed record ScreeningResult
    {
        public ScreeningResult(string text, int redactions)
        {
            Text = text;
            Redactions = redactions;
        }

        /// <summary>Gets the text with anything sensitive replaced.</summary>
        public string Text { get; }

        /// <summary>Gets how many replacements were made.</summary>
        public int Redactions { get; }

        /// <summary>Gets a value indicating whether anything was withheld.</summary>
        public bool Redacted => Redactions > 0;
    }

    /// <summary>
    /// Labels that mark the value beside them as something not to repeat.
    /// <summary>
    [GeneratedRegex(
        @"\b(password|passcode|passphrase|pin|secret|api[\s_-]?key|auth[\s_-]?token|bearer|private[\s_-]?key|security[\s_-]?code|cvv|cvc|otp|one[\s_-]?time[\s_-]?(?:code|password)|card[\s_-]?number)\b\s*[:=]?\s*\S+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        2000)]
    private static partial Regex LabelledSecretRegex { get; }

    /// <summary>A long digit run on its own, which is a card number far more often than anything else.</summary>
    [GeneratedRegex(
        @"(?<!\d)(?:\d[ \-]?){13,19}(?!\d)",
        RegexOptions.CultureInvariant,
        2000)]
    private static partial Regex LongDigitRunRegex { get; }

    /// <summary>
    /// Blanks a sequence of characters that looks like a credential: too long, too mixed, no
    /// spaces. Deliberately not matched against the visible spaces in a card number, so the two
    /// patterns do not fight over the same run.
    /// </summary>
    [GeneratedRegex(
        @"\b(?=[A-Za-z0-9!@#$%^&*_\-+=]{12,})(?=[^\s]*[0-9])(?=[^\s]*[A-Za-z])(?=[^\s]*[^A-Za-z0-9])[A-Za-z0-9!@#$%^&*_\-+=]{12,}\b",
        RegexOptions.CultureInvariant,
        2000)]
    private static partial Regex CredentialShapedRegex { get; }

    /// <summary>
    /// Replaces anything that looks like a secret in the given text.
    /// </summary>
    /// <param name="text">The text to screen. May be null.</param>
    /// <returns>
    /// The screened text and a count of what was withheld. A null or blank input comes back
    /// unchanged, so callers do not have to check first.
    /// </returns>
    public static ScreeningResult Screen(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ScreeningResult(string.Empty, 0);
        }

        var redactions = 0;
        var screened = LabelledSecretRegex.Replace(text, match =>
        {
            redactions++;

            // Keep the label, drop the value. "password: [redacted]" tells the model there is a
            // password here; a bare "[redacted]" does not.
            var separator = match.Value.IndexOfAny(new[] { ':', '=' });
            if (separator < 0)
            {
                var space = match.Value.IndexOf(' ');
                return space > 0
                    ? $"{match.Value[..(space + 1)]}{RedactionMarker}"
                    : RedactionMarker;
            }

            return $"{match.Value[..(separator + 1)]} {RedactionMarker}";
        });

        screened = LongDigitRunRegex.Replace(screened, _ =>
        {
            redactions++;
            return RedactionMarker;
        });

        screened = CredentialShapedRegex.Replace(screened, _ =>
        {
            redactions++;
            return RedactionMarker;
        });

        return new ScreeningResult(screened, redactions);
    }

    /// <summary>
    /// Screens an OCR result, keeping the text and the regions consistent with each other.
    /// </summary>
    /// <remarks>
    /// The region boxes are dropped when a redaction happened, because a caller using them to
    /// highlight would otherwise highlight the position of a secret and invite a second look at
    /// it. No text in this result has been sent anywhere; it has only been made less specific.
    /// </remarks>
    public static (OcrResult Ocr, bool Redacted) Screen(OcrResult ocr)
    {
        ArgumentNullException.ThrowIfNull(ocr);

        if (!ocr.HasText)
        {
            return (ocr, false);
        }

        var screened = Screen(ocr.Text);
        if (!screened.Redacted)
        {
            return (ocr, false);
        }

        var regions = ocr.Regions
            .Select(region => new OcrTextRegion(Screen(region.Text).Text, region.BoundingBox, region.Confidence))
            .Where(region => !string.IsNullOrWhiteSpace(region.Text))
            .ToArray();

        return (
            new OcrResult(screened.Text, ocr.Provider, ocr.Language, ocr.Confidence, regions),
            true);
    }

    /// <summary>
    /// Returns the warnings a result should carry given what the screener found.
    /// <para>
    /// A warning list that is never empty trains people to ignore it, so this only speaks up
    /// when something was actually withheld, unreadable, or reported as doubtful. Text that
    /// was read cleanly produces no warning at all.
    /// </para>
    /// </summary>
    public static IEnumerable<ScreenWarningKind> WarningsFor(OcrResult? ocr, bool wasRedacted)
    {
        if (wasRedacted)
        {
            yield return ScreenWarningKind.SensitiveContentSkipped;
        }

        if (ocr is null || !ocr.HasText)
        {
            yield return ScreenWarningKind.TextNotRecognized;
            yield break;
        }

        if (ocr.HasConfidence && ocr.Confidence < UnclearConfidence)
        {
            yield return ScreenWarningKind.OcrTextUnclear;
        }
    }

    /// <summary>
    /// Below this mean confidence the recognized text is treated as doubtful. Chosen to sit just
    /// under the point where a word is more likely wrong than right, rather than at a round
    /// number, because the cost of warning unnecessarily is only a line of text while the cost
    /// of not warning is quoting a serial number back to somebody.
    /// </summary>
    private const double UnclearConfidence = 0.6;
}
