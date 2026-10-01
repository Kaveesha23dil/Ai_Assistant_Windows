using System.Globalization;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Reports;

/// <summary>
/// Turns the name somebody asked for into a format this application can write.
/// <para>
/// Kept apart from the writers so that "pdf", "a pdf please", "PDF" and an empty string are one
/// decision made in one place. A planner produces whatever the request happened to contain, and
/// making each writer decide what counts as its own name would spread the fallback rule across
/// four files and let them disagree.
/// </para>
/// <para>
/// Every unrecognised name falls back to <see cref="ReportFormat.Markdown"/> rather than
/// failing. A request for "a report, ideally as a spreadsheet" should still produce a report; the
/// person asked for a report first and a format second, and refusing the whole thing because the
/// second part is unfamiliar would be the wrong way round.
/// </para>
/// </summary>
public static class ReportFormats
{
    /// <summary>Gets every format this build can write, least to most capable.</summary>
    public static IReadOnlyList<ReportFormat> All { get; } =
    [
        ReportFormat.Text,
        ReportFormat.Markdown,
        ReportFormat.Word,
        ReportFormat.Pdf,
    ];

    /// <summary>The format used when a request names none, or names one that is not written here.</summary>
    public const ReportFormat Default = ReportFormat.Markdown;

    /// <summary>Gets the file extension for a format, including the dot.</summary>
    public static string Extension(ReportFormat format) => format switch
    {
        ReportFormat.Text => ".txt",
        ReportFormat.Markdown => ".md",
        ReportFormat.Word => ".docx",
        ReportFormat.Pdf => ".pdf",
        _ => ".md",
    };

    /// <summary>Gets the name shown in an interface, for a person to choose from.</summary>
    public static string DisplayName(ReportFormat format) => format switch
    {
        ReportFormat.Text => "Plain text",
        ReportFormat.Markdown => "Markdown",
        ReportFormat.Word => "Word document",
        ReportFormat.Pdf => "PDF",
        _ => "Markdown",
    };

    /// <summary>Gets the sentence explaining why a named format was not understood.</summary>
    public static string UnrecognizedMessage(string? named) =>
        string.IsNullOrWhiteSpace(named)
            ? "I did not catch which file type you wanted, so I wrote Markdown."
            : $"I cannot write {named.Trim()} files, so I wrote Markdown instead.";

    /// <summary>
    /// Reads a format from a name, ignoring case and surrounding punctuation.
    /// <para>
    /// Also matches the common misspellings of the two abbreviations and the extension, because a
    /// planner asked for "docx" or ".pdf" plainly means the format and refusing it would be
    /// pedantry about a spelling rather than a safety decision.
    /// </para>
    /// </summary>
    public static ReportFormat Parse(string? value) => Parse(value, out _);

    /// <summary>
    /// Reads a format from a name, reporting whether it was one this build writes.
    /// <para>
    /// The reporting matters to the caller and not to the writers: the tool uses it to put a
    /// sentence in its answer when it had to substitute, because quietly writing Markdown for a
    /// request that said PDF would leave somebody with a file of the wrong type and no way to know
    /// until they opened it.
    /// </para>
    /// </summary>
    public static ReportFormat Parse(string? value, out bool wasRecognized)
    {
        wasRecognized = false;

        if (string.IsNullOrWhiteSpace(value))
        {
            return Default;
        }

        var words = Words(value);

        if (words.Count == 0)
        {
            return Default;
        }

        // A whole word at a time rather than the string as a whole, because the value reaching
        // here is whatever a plan wrote in its parameters and a plan writes phrases: "as a PDF",
        // "plain text", "Word document". Matching whole words is also what keeps "doc" from
        // matching inside "docx" and "md" from matching inside "markdown".
        for (var index = 0; index < words.Count; index++)
        {
            if (Single(words, index) is { } format)
            {
                wasRecognized = true;
                return format;
            }

            // Two-word names, checked after the single word so that "plain text" is read as one
            // name rather than as "plain", which alone means nothing.
            if (index + 1 < words.Count)
            {
                var pair = words[index] + words[index + 1];

                if (pair is "plaintext" or "worddocument" or "richtext" or "plainfile")
                {
                    wasRecognized = true;
                    return pair.StartsWith("word", StringComparison.Ordinal) ||
                        pair.StartsWith("rich", StringComparison.Ordinal)
                            ? ReportFormat.Word
                            : ReportFormat.Text;
                }
            }
        }

        return Default;
    }

    /// <summary>The single-word names, mapped to the format they mean.</summary>
    private static ReportFormat? Single(System.Collections.Generic.List<string> words, int index) =>
        words[index] switch
        {
            "txt" or "text" or "plain" => ReportFormat.Text,
            "md" or "markdown" or "mdown" => ReportFormat.Markdown,
            "doc" or "docx" or "word" => ReportFormat.Word,
            "pdf" => ReportFormat.Pdf,
            _ => null,
        };

    /// <summary>
    /// Splits a name into the words in it, lowercased, dropping punctuation.
    /// <para>
    /// Separators are dropped rather than replaced so that "plain-text", "plain.text", and
    /// "plaintext" all arrive as the one word "plaintext". A trailing dot is dropped with them,
    /// which is what makes ".pdf" and "pdf." the same thing.
    /// </para>
    /// </summary>
    private static System.Collections.Generic.List<string> Words(string value)
    {
        var words = new System.Collections.Generic.List<string>();
        var builder = new System.Text.StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                continue;
            }

            if (builder.Length > 0)
            {
                words.Add(builder.ToString());
                builder.Clear();
            }
        }

        if (builder.Length > 0)
        {
            words.Add(builder.ToString());
        }

        return words;
    }
}