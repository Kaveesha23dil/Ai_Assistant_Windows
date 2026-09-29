using System.Text;

namespace WindowsAIAssistant.Core.Models.Knowledge;

/// <summary>
/// Where a passage came from, in the terms a person can turn to and find.
/// <para>
/// Every field here was reported by the format itself. A page number came from a PDF's own
/// pages, a sheet name from a workbook's own sheet, a heading from a word processor's own
/// heading style. Nothing is inferred from a character offset, because an offset nobody can
/// check is worse than no reference at all: it looks like a citation and behaves like a guess.
/// </para>
/// <para>
/// A reference may legitimately name several things, because a chunk of a presentation can
/// cover a slide and the notes attached to it. What it may never do is name something the
/// extractor did not report, which is why the properties are nullable and the formatter has a
/// case for having nothing to show rather than a default.
/// </para>
/// </summary>
public sealed record KnowledgeSourceReference
{
    /// <summary>Gets the page number, when the document is paginated.</summary>
    public int? PageNumber { get; init; }

    /// <summary>Gets the slide number, for a presentation.</summary>
    public int? SlideNumber { get; init; }

    /// <summary>Gets the worksheet name, for a workbook.</summary>
    public string? SheetName { get; init; }

    /// <summary>Gets the heading the passage sits under, when the format has headings.</summary>
    public string? Heading { get; init; }

    /// <summary>Gets the section's own name, as the document called it.</summary>
    public string? Section { get; init; }

    /// <summary>
    /// Gets the first line of the passage, when the format is line-oriented. Offered because a
    /// text file has nothing else, and "line 412" is a real thing somebody can go and look at.
    /// </summary>
    public int? FirstLine { get; init; }

    /// <summary>Gets the last line of the passage, when the format is line-oriented.</summary>
    public int? LastLine { get; init; }

    /// <summary>
    /// Gets a value indicating whether anything at all was reported.
    /// <para>
    /// A passage with no reference is still worth indexing — it can still answer a question —
    /// but it must never be cited as though it came from somewhere, so the formatter and the
    /// interface both ask this first.
    /// </para>
    /// </summary>
    public bool HasAny =>
        PageNumber.HasValue
        || SlideNumber.HasValue
        || !string.IsNullOrWhiteSpace(SheetName)
        || !string.IsNullOrWhiteSpace(Heading)
        || !string.IsNullOrWhiteSpace(Section)
        || FirstLine.HasValue;

    /// <summary>
    /// Gets the reference as a short phrase to put in a sentence or a source list.
    /// <para>
    /// Parts are joined in the order a reader would say them, and a part is included only when
    /// the document actually supplied it. A reference that said "page  of " would be worse than
    /// none, and inventing "page 1" for a passage that has no page is exactly the fabrication
    /// this type exists to prevent.
    /// </para>
    /// </summary>
    public string Display
    {
        get
        {
            var parts = new List<string>();

            if (PageNumber is { } page)
            {
                parts.Add($"page {page}");
            }

            if (SlideNumber is { } slide)
            {
                parts.Add($"slide {slide}");
            }

            if (!string.IsNullOrWhiteSpace(SheetName))
            {
                parts.Add($"sheet \"{SheetName.Trim()}\"");
            }

            if (!string.IsNullOrWhiteSpace(Heading))
            {
                parts.Add(Heading.Trim());
            }
            else if (!string.IsNullOrWhiteSpace(Section))
            {
                parts.Add(Section.Trim());
            }

            if (parts.Count == 0)
            {
                if (FirstLine is { } from && LastLine is { } to && to > from)
                {
                    parts.Add($"lines {from}\u2013{to}");
                }
                else if (FirstLine is { } single)
                {
                    parts.Add($"line {single}");
                }
            }

            return string.Join(", ", parts);
        }
    }

    /// <summary>
    /// Reads a reference back from its stored form, for a repository that keeps it as text.
    /// <para>
    /// The four values are the ones a citation can be checked against, so they are stored rather
    /// than the phrase: the phrase is assembled on the way out for whichever of them exist, and
    /// storing a rendered string would freeze today's wording into the index.
    /// </para>
    /// </summary>
    public string ToStorageString()
    {
        var builder = new StringBuilder();

        Append(builder, "p", PageNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(builder, "s", SlideNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(builder, "sheet", SheetName);
        Append(builder, "h", Heading);
        Append(builder, "sec", Section);
        Append(builder, "l1", FirstLine?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(builder, "l2", LastLine?.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return builder.ToString();

        static void Append(StringBuilder builder, string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.Append('|');
            }

            builder.Append(key).Append(':').Append(value);
        }
    }

    /// <summary>
    /// Rebuilds a reference from its stored form. Text that does not parse yields no reference
    /// rather than an exception: a citation is not worth failing a search over.
    /// </summary>
    public static KnowledgeSourceReference FromStorageString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new KnowledgeSourceReference();
        }

        int? page = null;
        int? slide = null;
        int? firstLine = null;
        int? lastLine = null;
        string? sheet = null;
        string? heading = null;
        string? section = null;

        foreach (var part in value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var key = part[..separator];
            var text = part[(separator + 1)..];

            switch (key)
            {
                case "p":
                    page = ParseNumber(text);
                    break;
                case "s":
                    slide = ParseNumber(text);
                    break;
                case "l1":
                    firstLine = ParseNumber(text);
                    break;
                case "l2":
                    lastLine = ParseNumber(text);
                    break;
                case "sheet":
                    sheet = text;
                    break;
                case "h":
                    heading = text;
                    break;
                case "sec":
                    section = text;
                    break;
                default:
                    break;
            }
        }

        return new KnowledgeSourceReference
        {
            PageNumber = page,
            SlideNumber = slide,
            SheetName = sheet,
            Heading = heading,
            Section = section,
            FirstLine = firstLine,
            LastLine = lastLine,
        };
    }

    private static int? ParseNumber(string text) =>
        int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
