using System.Globalization;

namespace WindowsAIAssistant.Application.Voice.Text;

/// <summary>
/// Builds the short, spoken sentences the assistant replies with.
/// <para>
/// Spoken output is a different medium from displayed output, so the numbers are rounded to
/// something a person can hear correctly, the phrasing stays short enough to finish quickly,
/// and nothing sensitive is ever formatted into a sentence.
/// </para>
/// </summary>
public static class ResponseTextFormatter
{
    private const string Ellipsis = "...";

    private static readonly string[] BinaryUnits = ["bytes", "KB", "MB", "GB", "TB"];

    /// <summary>Formats a byte count in the largest unit that keeps the number readable.</summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 bytes";
        }

        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024.0 && unitIndex < BinaryUnits.Length - 1)
        {
            value /= 1024.0;
            unitIndex++;
        }

        var format = unitIndex == 0 ? "F0" : "F1";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{value.ToString(format, CultureInfo.InvariantCulture)} {BinaryUnits[unitIndex]}");
    }

    /// <summary>Formats a percentage as a whole number.</summary>
    public static string FormatPercentage(double percentage) =>
        Math.Round(percentage).ToString("F0", CultureInfo.InvariantCulture);

    /// <summary>Formats a clock time without a culture-specific suffix.</summary>
    public static string FormatTime(DateTimeOffset value) =>
        value.ToString("h:mm tt", CultureInfo.InvariantCulture);

    /// <summary>Formats a date in a long, unambiguous form.</summary>
    public static string FormatDate(DateTimeOffset value) =>
        value.ToString("dddd, d MMMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>Formats a duration as whole hours and minutes, or just minutes when short.</summary>
    public static string FormatDuration(TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
        {
            return "an unknown amount of time";
        }

        var hours = (int)value.TotalHours;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours} hour{(hours == 1 ? string.Empty : "s")} and {value.Minutes} minutes")
            : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(value.Minutes, 1)} minutes");
    }

    /// <summary>
    /// Shortens text for speech, cutting on a word boundary and appending an ellipsis so the
    /// listener knows the reply was shortened rather than finished.
    /// <para>
    /// The result never exceeds <paramref name="maximumLength"/>, marker included. Cutting to
    /// the limit and then appending the marker would overshoot it by the marker's own length,
    /// which matters because the limit is what keeps a reply from running on past the user's
    /// attention.
    /// </para>
    /// </summary>
    public static string TruncateForSpeech(string text, int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (maximumLength <= 0)
        {
            return string.Empty;
        }

        if (text.Length <= maximumLength)
        {
            return text;
        }

        if (maximumLength <= Ellipsis.Length)
        {
            return Ellipsis[..maximumLength];
        }

        var budget = maximumLength - Ellipsis.Length;
        var cut = text.LastIndexOf(' ', Math.Min(budget, text.Length - 1), budget);
        if (cut <= 0)
        {
            cut = budget;
        }

        return string.Concat(text.AsSpan(0, cut).TrimEnd(), Ellipsis);
    }
}
