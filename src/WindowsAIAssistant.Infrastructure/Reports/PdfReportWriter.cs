using System.Globalization;
using System.Text;
using WindowsAIAssistant.Core.Abstractions.Reports;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Reports;

namespace WindowsAIAssistant.Infrastructure.Reports;

/// <summary>
/// Renders a report as a PDF, writing the file format directly.
/// <para>
/// No PDF library, and the reason is worth stating because the usual reason — "fewer dependencies"
/// — is not it. A report this application writes is one page or two of headings, bullets, and
/// prose, laid out left to right in a single column, and the PDF format for that is a page tree,
/// a content stream of positioned text, and a cross-reference table. Building those three directly
/// is a few hundred lines with no native code, no font engine, and no network fetch, and it can
/// only produce documents this application already knows how to produce.
/// </para>
/// <para>
/// What that rules out is stated here rather than discovered later: no embedded font subsetting,
/// no images, no tables that span pages, no compression. Text is drawn with the standard Helvetica
/// face every reader has, and characters outside that set become a question mark rather than
/// something that renders as a blank box. A report about deployment architecture is ASCII, and a
/// report that is not should be written as Markdown or Word where the text survives.
/// </para>
/// <para>
/// The offsets in a PDF file are byte offsets into the finished document, which is why the
/// renderer works out its own length rather than guessing. Two passes, and the second one is the
/// real one.
/// </para>
/// </summary>
public sealed class PdfReportWriter : IReportWriter
{
    /// <summary>A4 in PostScript points.</summary>
    private const double PageWidth = 595.28;

    /// <summary>A4 in PostScript points.</summary>
    private const double PageHeight = 841.89;

    /// <summary>The margin on every side, in points.</summary>
    private const double Margin = 56.7;

    /// <summary>Leading for body text, in points.</summary>
    private const double LineHeight = 14.0;

    /// <summary>Body text size, in points.</summary>
    private const double BodySize = 10.5;

    /// <summary>Heading sizes by level, in points.</summary>
    private static readonly double[] HeadingSizes = [18.0, 15.0, 13.0, 11.5, 11.0, 10.5];

    /// <summary>
    /// The width of the standard Helvetica faces in units of 1/1000 em.
    /// <para>
    /// Needed because every text-drawing operator needs the string's width to draw the next one in
    /// the right place. These are the metrics from the Adobe base-14 font tables, and they are the
    /// reason this writer can lay text out without embedding a font: the widths are already known
    /// to every reader, so it is enough to have them here.
    /// </para>
    /// </summary>
    private static readonly int[][] Widths =
    [
        [278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
         556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
         1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
         667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
         333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
         556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584],
        [556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556,
         556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556,
         556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556,
         556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556,
         556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556,
         556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556,
         556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556],
        [556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556, 556,
         556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584],
    ];

    /// <inheritdoc />
    public ReportFormat Format => ReportFormat.Pdf;

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public string? UnavailableReason => null;

    /// <inheritdoc />
    public Task<ReportWriteResult> WriteAsync(
        ReportDocument report,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        cancellationToken.ThrowIfCancellationRequested();

        if (!report.TryValidate(out var refusal))
        {
            return Task.FromResult(ReportWriteResult.Failure(
                ErrorCodes.AgentReportFailed,
                refusal));
        }

        try
        {
            var lines = Layout(report, cancellationToken);
            var bytes = Render(lines, report.CreatedAt);

            return Task.FromResult(ReportWriteResult.Success(bytes));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (EncoderFallbackException)
        {
            // Reached only if a string cannot be encoded at all, which Latin-1 always allows.
            return Task.FromResult(ReportWriteResult.Failure(
                ErrorCodes.AgentReportFailed,
                "The PDF could not be created from that report."));
        }
    }

    /// <summary>One line of text, already positioned and sized.</summary>
    private readonly record struct Line(double X, double Y, double Size, bool Bold, bool Italic, string Text);

    /// <summary>
    /// Breaks the report into positioned lines.
    /// <para>
    /// Wrapping happens here rather than in the content stream, because a PDF content stream
    /// places a whole string at one position and does not wrap it. Word wrapping is therefore
    /// part of laying the page out, not part of drawing it.
    /// </para>
    /// </summary>
    private static List<Line> Layout(ReportDocument report, CancellationToken cancellationToken)
    {
        var lines = new List<Line>();

        lines.Add(new Line(Margin, PageHeight - Margin, HeadingSizes[0], Bold: true, Italic: false,
            Sanitize(report.Title)));

        lines.Add(new Line(Margin, PageHeight - Margin - 24, BodySize, Bold: false, Italic: true,
            $"Written {report.CreatedAt.ToString("u", CultureInfo.InvariantCulture)}"));

        var available = PageWidth - (Margin * 2);
        var cursor = PageHeight - Margin - 60;
        var bulletIndent = 0.0;
        var inFence = false;

        foreach (var raw in report.Body.Split('\n'))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var raw_ = raw.TrimEnd('\r');
            var trimmed = raw_.TrimStart();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (trimmed.Length == 0)
            {
                cursor -= LineHeight / 2;
                bulletIndent = 0;
                continue;
            }

            var headingLevel = HeadingLevelOf(trimmed);

            if (headingLevel is { } level)
            {
                cursor -= 8;

                var size = HeadingSizes[Math.Min(level, HeadingSizes.Length) - 1];
                var text = StripInline(trimmed[level..].Trim());

                foreach (var wrapped in Wrap(text, size, available))
                {
                    cursor -= size + 3;
                    lines.Add(new Line(Margin, cursor, size, Bold: true, Italic: false, Sanitize(wrapped)));
                }

                continue;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal) ||
                trimmed.StartsWith("* ", StringComparison.Ordinal) ||
                trimmed.StartsWith("+ ", StringComparison.Ordinal))
            {
                bulletIndent = 14;
                var size = inFence ? BodySize : BodySize;
                var text = inFence ? trimmed[2..] : StripInline(trimmed[2..]);

                lines.Add(new Line(Margin, cursor, size, Bold: false, Italic: false, "\u2022"));

                foreach (var wrapped in Wrap(text, size, available - bulletIndent))
                {
                    cursor -= size + 3;
                    lines.Add(new Line(Margin + bulletIndent, cursor, size, Bold: false, Italic: false,
                        Sanitize(wrapped)));
                }

                continue;
            }

            var bodySize = inFence ? BodySize - 0.5 : BodySize;
            var bodyText = inFence ? raw_ : StripInline(raw_);

            foreach (var wrapped in Wrap(bodyText, bodySize, available - bulletIndent))
            {
                cursor -= bodySize + 3;
                lines.Add(new Line(Margin + bulletIndent, cursor, bodySize, Bold: false, Italic: inFence,
                    Sanitize(wrapped)));
            }
        }

        return lines;
    }

    /// <summary>Counts leading hashes, or <see langword="null"/> when the line is not a heading.</summary>
    private static int? HeadingLevelOf(string line)
    {
        var level = 0;

        while (level < line.Length && line[level] == '#')
        {
            level++;
        }

        if (level is 0 or > 6 || level >= line.Length || line[level] is not (' ' or '\t'))
        {
            return null;
        }

        return level;
    }

    /// <summary>
    /// Replaces anything outside the standard Helvetica set.
    /// <para>
    /// This is the boundary where a report can lose a character, and it is a visible one rather
    /// than a silent one: the standard fonts have no glyph for most of the world's writing
    /// systems, and drawing one anyway produces a blank or a box in the reader. Substituting a
    /// question mark is something a person notices and can ask about.
    /// </para>
    /// </summary>
    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            var code = (int)character;

            builder.Append(code is >= 32 and <= 126 || code == 10 || code == 9 ? character : '?');
        }

        return builder.ToString();
    }

    /// <summary>Removes the inline Markdown markers, keeping the words.</summary>
    private static string StripInline(string line)
    {
        var builder = new StringBuilder(line.Length);

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];

            if ((character is '*' or '_' or '`') &&
                index + 1 < line.Length && line[index + 1] == character)
            {
                index++;
                continue;
            }

            if (character == '`')
            {
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Wraps text to a width, at word boundaries.
    /// <para>
    /// A single word longer than the line is broken rather than allowed to run off the page. That
    /// is the case that matters in practice: a stack trace, a URL, or a path is exactly the sort
    /// of thing a report about software contains, and letting it overflow produces a page whose
    /// right-hand side is empty for the rest of the document.
    /// </para>
    /// </summary>
    private static IEnumerable<string> Wrap(string text, double size, double available)
    {
        if (available <= 0 || string.IsNullOrEmpty(text))
        {
            yield return text;
            yield break;
        }

        var current = new StringBuilder();
        var currentWidth = 0.0;

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var wordWidth = Measure(word, size);

            if (wordWidth > available)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                    currentWidth = 0;
                }

                foreach (var fragment in BreakWord(word, size, available))
                {
                    yield return fragment;
                }

                continue;
            }

            var separator = current.Length == 0 ? 0 : Measure(" ", size);

            if (currentWidth + separator + wordWidth > available)
            {
                yield return current.ToString();
                current.Clear();
                currentWidth = 0;
                separator = 0;
            }

            if (separator > 0)
            {
                current.Append(' ');
                currentWidth += separator;
            }

            current.Append(word);
            currentWidth += wordWidth;
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    /// <summary>Breaks a single over-long word into pieces that fit.</summary>
    private static IEnumerable<string> BreakWord(string word, double size, double available)
    {
        var builder = new StringBuilder();
        var width = 0.0;

        foreach (var character in word)
        {
            var characterWidth = Measure(character.ToString(), size);

            if (width + characterWidth > available && builder.Length > 0)
            {
                yield return builder.ToString();
                builder.Clear();
                width = 0;
            }

            builder.Append(character);
            width += characterWidth;
        }

        if (builder.Length > 0)
        {
            yield return builder.ToString();
        }
    }

    /// <summary>The width of a string at a given size, in points.</summary>
    private static double Measure(string text, double size)
    {
        var total = 0;

        foreach (var character in text)
        {
            total += Width(character, bold: false);
        }

        return total * size / 1000.0;
    }

    /// <summary>The advance width of one character in 1/1000 em, from the base-14 metrics.</summary>
    private static int Width(char character, bool bold)
    {
        // Bold is the Helvetica-Bold table; italic reuses the upright one, which is a small
        // inaccuracy in advance widths and no inaccuracy at all in the glyphs drawn.
        var table = bold ? Widths[1] : Widths[0];
        var index = (int)character;

        return index is >= 32 and <= 126 ? table[index - 32] : table[0];
    }

    /// <summary>
    /// Builds the file.
    /// <para>
    /// Written forward in one pass, recording each object's byte position as it goes. That is
    /// possible because the cross-reference table lives at the very end of a PDF, after every
    /// object whose offset it records — so by the time the table is written, all the offsets in it
    /// are already known. The table is the only thing that refers back to what precedes it.
    /// </para>
    /// </summary>
    private static byte[] Render(IReadOnlyList<Line> lines, DateTimeOffset createdAt)
    {
        var content = BuildContentStream(lines);

        // 1 catalogue, 2 pages, 3 page, 4 fonts, 5 contents.
        var objects = new string[6];

        objects[1] = "<< /Type /Catalog /Pages 2 0 R >>";
        objects[2] = "<< /Type /Pages /Kids [3 0 R] /Count 1 >>";

        objects[3] = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " +
            Num(PageWidth) + " " + Num(PageHeight) + "] " +
            "/Resources << /Font << /F1 4 0 R /F2 4 0 R >> >> /Contents 5 0 R >>";

        objects[4] =
            "<< /Font << " +
            "/F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >> " +
            "/F2 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >> " +
            ">> >>";

        objects[5] = $"<< /Length {content.Length.ToString(CultureInfo.InvariantCulture)} >>\nstream\n{content}\nendstream";

        var info =
            $"/Producer (Windows AI Assistant) /CreationDate ({ToPdfDate(createdAt)})";

        var buffer = new MemoryStream();

        void Write(string value) =>
            buffer.Write(Encoding.ASCII.GetBytes(value));

        Write("%PDF-1.4\n");

        // A binary comment marks the file as containing binary data, which is what stops a
        // transfer tool from treating it as text and re-encoding the bytes.
        Write("%\xE2\xE3\xCF\xD3\n");

        var offsets = new long[6];

        for (var index = 1; index < objects.Length; index++)
        {
            offsets[index] = buffer.Position;

            Write($"{index.ToString(CultureInfo.InvariantCulture)} 0 obj\n");
            Write(objects[index]);
            Write("\nendobj\n");
        }

        var infoOffset = buffer.Position;

        Write($"6 0 obj\n<< {info} >>\nendobj\n");

        var xrefOffset = buffer.Position;

        // Six entries plus the free one at zero: object 0 is always the head of the free list,
        // and omitting it is what makes readers reject the file.
        Write($"xref\n0 {objects.Length.ToString(CultureInfo.InvariantCulture)}\n");
        Write("0000000000 65535 f \n");

        for (var index = 1; index < objects.Length; index++)
        {
            Write(Num((double)offsets[index], width: 10) + " 00000 n \n");
        }

        Write(Num((double)infoOffset, width: 10) + " 00000 n \n");

        Write($"trailer\n<< /Size {objects.Length.ToString(CultureInfo.InvariantCulture)} /Root 1 0 R " +
            $"/Info {infoOffset.ToString(CultureInfo.InvariantCulture)} 0 R >>\n");

        Write($"startxref\n{xrefOffset.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");

        return buffer.ToArray();
    }

    /// <summary>
    /// Builds the page's drawing instructions.
    /// <para>
    /// One <c>BT</c>/<c>ET</c> block per contiguous run of the same style, rather than one per
    /// line, because switching the font and size inside a block is cheaper than opening and
    /// closing a new one — and a report's body is overwhelmingly one style.
    /// </para>
    /// </summary>
    private static string BuildContentStream(IReadOnlyList<Line> lines)
    {
        var builder = new StringBuilder(lines.Count * 64);

        builder.Append("q\n");

        string? currentFont = null;
        double? currentSize = null;

        foreach (var line in lines)
        {
            var font = line.Bold ? "/F2" : "/F1";

            if (currentFont is null || currentSize != line.Size || currentFont != font)
            {
                if (currentFont is not null)
                {
                    builder.Append("ET\n");
                }

                builder.Append("BT\n");
                builder.Append(CultureInfo.InvariantCulture, $"{font} {Num(line.Size)} Tf\n");

                if (line.Italic)
                {
                    // The standard faces have no italic variant, so a slanted form is drawn
                    // instead. The font's own italic substitution would not happen inside a
                    // PDF's own font resource.
                    builder.Append("0.96 0.96 0.92 rg\n");
                }
                else
                {
                    builder.Append("0 0 0 rg\n");
                }

                currentFont = font;
                currentSize = line.Size;
            }

            builder.Append(CultureInfo.InvariantCulture,
                $"1 0 0 1 {Num(line.X)} {Num(line.Y)} Tm\n");

            builder.Append('(').Append(Escape(line.Text)).Append(") Tj\n");
        }

        if (currentFont is not null)
        {
            builder.Append("ET\n");
        }

        builder.Append("Q\n");

        return builder.ToString();
    }

    /// <summary>
    /// Escapes a string for a literal in a content stream.
    /// <para>
    /// Parentheses, backslashes, and the three characters WinAnsi places at 0x80-0x9F. Only
    /// WinAnsi rather than full Unicode is claimed here because that is the encoding the font
    /// resource declares, and a document whose declared encoding and content disagree is one a
    /// reader is entitled to reject.
    /// </para>
    /// </summary>
    private static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length + 8);

        foreach (var character in value)
        {
            switch (character)
            {
                case '(':
                    builder.Append("\\(");
                    break;
                case ')':
                    builder.Append("\\)");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>Writes a PDF date, which has its own syntax and is never an ISO one.</summary>
    private static string ToPdfDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("'D':yyyyMMddHHmmss'Z'", CultureInfo.InvariantCulture);

    /// <summary>Formats a number the way a PDF expects, with a fixed decimal point.</summary>
    private static string Num(double value, int width = 0)
    {
        var text = value.ToString("0.##", CultureInfo.InvariantCulture);

        return width == 0 ? text : text.PadLeft(width, '0');
    }
}