using System.Text;
using WindowsAIAssistant.Core.Abstractions.Reports;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Reports;

namespace WindowsAIAssistant.Infrastructure.Reports;

/// <summary>
/// Renders a report as plain text or as Markdown.
/// <para>
/// One writer for both because the difference is only the headings: both are UTF-8 text, both are
/// written with a single call, and splitting them would produce two classes that differ in a
/// heading style and a comment.
/// </para>
/// <para>
/// Markdown keeps its markup. A report written as Markdown is meant to be read as Markdown, and
/// stripping the markers would produce a worse file for the format the person actually chose. The
/// plain-text writer is the one that removes them, because a text file with <c>##</c> in it looks
/// like a mistake.
/// </para>
/// </summary>
public sealed class TextReportWriter : IReportWriter
{
    private readonly ReportFormat _format;

    public TextReportWriter(ReportFormat format = ReportFormat.Markdown)
    {
        if (format is not (ReportFormat.Text or ReportFormat.Markdown))
        {
            throw new ArgumentOutOfRangeException(
                nameof(format),
                format,
                "This writer produces text formats only.");
        }

        _format = format;
    }

    /// <inheritdoc />
    public ReportFormat Format => _format;

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
                Core.Common.ErrorCodes.AgentReportFailed,
                refusal));
        }

        var builder = new StringBuilder(report.Body.Length + 128);

        builder.Append('#').Append(' ').AppendLine(report.Title).AppendLine();

        // The timestamp is written as plain text rather than as markup in either case, so a plain
        // text file does not gain emphasis markers it has no way to render.
        builder.Append("Written ")
            .AppendLine(report.CreatedAt.ToString("u", System.Globalization.CultureInfo.InvariantCulture))
            .AppendLine();

        var body = _format == ReportFormat.Text ? StripMarkdown(report.Body) : report.Body;

        builder.AppendLine(body);

        // A UTF-8 encoding without a byte-order mark. A BOM is invisible in a text editor and
        // visible to whatever reads the file next, which is exactly the wrong way round for a
        // report somebody is meant to read.
        return Task.FromResult(ReportWriteResult.Success(
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(builder.ToString())));
    }

    /// <summary>
    /// Removes Markdown syntax, keeping the words and the structure they described.
    /// <para>
    /// Reduced rather than complete: headings lose their markers but keep their breaks, emphasis
    /// markers go, list bullets stay as dashes, and fenced code blocks lose only the fences.
    /// A complete Markdown parser is not worth adding to write a text file, and a lossy one that
    /// removes the words would be worse than this.
    /// </para>
    /// </summary>
    private static string StripMarkdown(string body)
    {
        var builder = new StringBuilder(body.Length);
        var inFence = false;

        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');

            if (trimmed.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            // Inside a fence the text is literal, so nothing in it is touched at all.
            builder.AppendLine(inFence ? trimmed : StripInline(trimmed));
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>Removes the inline markers from one line.</summary>
    private static string StripInline(string line)
    {
        var trimmed = line.AsSpan().TrimStart();

        // Leading hashes are a heading, not content. Keep the words and drop the level, and keep
        // a blank line's worth of separation so headings still read as headings.
        var headingLevel = 0;
        while (headingLevel < trimmed.Length && trimmed[headingLevel] == '#')
        {
            headingLevel++;
        }

        var working = headingLevel > 0 ? trimmed[headingLevel..].TrimStart() : line;

        var builder = new StringBuilder(working.Length);

        for (var index = 0; index < working.Length; index++)
        {
            var character = working[index];

            var emphasis = character is '*' or '_';

            // Only a doubled marker is emphasis; a single underscore inside a word is part of it.
            if (emphasis && index + 1 < working.Length && working[index + 1] == character)
            {
                index++;
                continue;
            }

            var isLinkMarker = character == '[' || character == ']';

            if (isLinkMarker)
            {
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}