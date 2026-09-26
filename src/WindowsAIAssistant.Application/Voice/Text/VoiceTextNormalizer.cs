using System.Globalization;
using System.Text;

namespace WindowsAIAssistant.Application.Voice.Text;

/// <summary>
/// Turns a raw transcript into the canonical form the intent rules match against.
/// <para>
/// Speech recognition output is inconsistent: casing varies, curly apostrophes appear,
/// punctuation is dropped, and users say "fifty" where they mean "50". Normalizing once, up
/// front, is what allows the rules to accept many phrasings without duplicating alternatives
/// in every pattern.
/// </para>
/// </summary>
public static class VoiceTextNormalizer
{
    private static readonly IReadOnlyDictionary<string, int> Units = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["zero"] = 0,
        ["one"] = 1,
        ["two"] = 2,
        ["three"] = 3,
        ["four"] = 4,
        ["five"] = 5,
        ["six"] = 6,
        ["seven"] = 7,
        ["eight"] = 8,
        ["nine"] = 9,
        ["ten"] = 10,
        ["eleven"] = 11,
        ["twelve"] = 12,
        ["thirteen"] = 13,
        ["fourteen"] = 14,
        ["fifteen"] = 15,
        ["sixteen"] = 16,
        ["seventeen"] = 17,
        ["eighteen"] = 18,
        ["nineteen"] = 19
    };

    private static readonly IReadOnlyDictionary<string, int> Tens = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["ten"] = 10,
        ["twenty"] = 20,
        ["thirty"] = 30,
        ["forty"] = 40,
        ["fifty"] = 50,
        ["sixty"] = 60,
        ["seventy"] = 70,
        ["eighty"] = 80,
        ["ninety"] = 90
    };

    private static readonly (string From, string To)[] PhraseFixups =
    [
        ("wi fi", "wifi"),
        ("w i fi", "wifi"),
        ("c sharp", "c#"),
        ("c plus plus", "c++"),
        ("visual studio code", "vs code")
    ];

    /// <summary>
    /// Produces the canonical lowercase form: apostrophes and hyphens preserved, other
    /// punctuation replaced by spaces, number words converted to digits, and a few
    /// multi-word names folded to the form the rules expect.
    /// </summary>
    public static string Normalize(string? transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return string.Empty;
        }

        var lowered = transcript.Trim().ToLowerInvariant().Replace('\u2019', '\'');

        var builder = new StringBuilder(lowered.Length);
        foreach (var character in lowered)
        {
            if (char.IsLetterOrDigit(character) || character is '\'' or '-' or '#' or '.' or '+' or '%')
            {
                builder.Append(character);
            }
            else
            {
                builder.Append(' ');
            }
        }

        var collapsed = CollapseWhitespace(builder.ToString());
        collapsed = ReplaceNumberWords(collapsed);

        foreach (var (from, to) in PhraseFixups)
        {
            collapsed = collapsed.Replace(from, to, StringComparison.Ordinal);
        }

        return collapsed.Trim();
    }

    /// <summary>
    /// Converts a normalized parameter value back into something a search engine or a file
    /// index can use, undoing the fix-ups that only exist for rule matching.
    /// </summary>
    public static string ToQueryText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value
            .Replace("c#", "C#", StringComparison.Ordinal)
            .Replace("c++", "C++", StringComparison.Ordinal)
            .Trim();
    }

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasSpace = true;

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                }

                previousWasSpace = true;
                continue;
            }

            builder.Append(character);
            previousWasSpace = false;
        }

        return builder.ToString();
    }

    private static string ReplaceNumberWords(string value)
    {
        var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return value;
        }

        var output = new List<string>(tokens.Length);

        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];

            if (string.Equals(token, "hundred", StringComparison.Ordinal))
            {
                if (output.Count > 0 && int.TryParse(output[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var previous))
                {
                    output[^1] = (previous * 100).ToString(CultureInfo.InvariantCulture);
                    continue;
                }

                output.Add("100");
                continue;
            }

            if (Tens.TryGetValue(token, out var tens))
            {
                var value2 = tens;
                if (index + 1 < tokens.Length
                    && Units.TryGetValue(tokens[index + 1], out var unit)
                    && unit is > 0 and < 10)
                {
                    value2 += unit;
                    index++;
                }

                output.Add(value2.ToString(CultureInfo.InvariantCulture));
                continue;
            }

            if (Units.TryGetValue(token, out var single))
            {
                output.Add(single.ToString(CultureInfo.InvariantCulture));
                continue;
            }

            output.Add(token);
        }

        return string.Join(' ', output);
    }
}
