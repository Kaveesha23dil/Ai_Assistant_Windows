using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WindowsAIAssistant.Core.Abstractions.Reports;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Reports;

namespace WindowsAIAssistant.Infrastructure.Reports;

/// <summary>
/// Renders a report as a Word document.
/// <para>
/// Built with the Open XML SDK rather than by writing a package's parts by hand, for one
/// practical reason: a <c>.docx</c> is a zip of XML parts with a content-types index, and a
/// hand-built one that is off by a single relationship is a file Word offers to repair. The SDK
/// writes the parts and the relationships together, and is the same library the document reader
/// already uses to read such a file back.
/// </para>
/// <para>
/// Headings and bullets are turned into real Word styles rather than bold runs. A report that
/// somebody opens and edits is the point of this format, and a document whose headings are bold
/// text cannot be re-ordered, re-styled, or put in a table of contents.
/// </para>
/// <para>
/// The bytes are built in memory and handed back rather than written to a path, so the tool that
/// calls this is still the only thing that decides where a file goes.
/// </para>
/// </summary>
public sealed class WordReportWriter : IReportWriter
{

    /// <inheritdoc />
    public ReportFormat Format => ReportFormat.Word;

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
            using var stream = new MemoryStream();

            using (var package = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
            {
                var main = package.AddMainDocumentPart();
                main.Document = new Document(new Body());
                var body = main.Document.Body!;

                body.Append(Heading(report.Title, level: 1));
                body.Append(Paragraph(
                    $"Written {report.CreatedAt.ToString("u", System.Globalization.CultureInfo.InvariantCulture)}",
                    italic: true));

                AppendBody(body, report.Body, cancellationToken);

                // A section properties element has to be the last child of the body, and omitting
                // it produces a document Word will rewrite on open.
                body.Append(new SectionProperties(
                    new PageSize { Width = 11906U, Height = 16838U },
                    new PageMargin
                    {
                        Top = 1440,
                        Right = 1440U,
                        Bottom = 1440,
                        Left = 1440U,
                        Header = 720U,
                        Footer = 720U,
                        Gutter = 0U,
                    }));

                main.Document.Save();
            }

            return Task.FromResult(ReportWriteResult.Success(stream.ToArray()));
        }
        catch (OpenXmlPackageException)
        {
            // The document could not be assembled. The sentence names the format and nothing
            // else: the exception's text is a part path, which is this application's business.
            return Task.FromResult(ReportWriteResult.Failure(
                ErrorCodes.AgentReportFailed,
                "The Word document could not be created just now."));
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult(ReportWriteResult.Failure(
                ErrorCodes.AgentReportFailed,
                "The Word document could not be created from that report."));
        }
    }

    /// <summary>
    /// Walks the Markdown body and produces paragraphs for it.
    /// <para>
    /// Line-oriented rather than a full Markdown parse, and the reason is worth stating: the
    /// body here is what earlier steps produced, so it is headings, bullets, and prose. Anything
    /// richer is rendered as the text it is, which loses formatting rather than losing words.
    /// </para>
    /// </summary>
    private static void AppendBody(Body body, string markdown, CancellationToken cancellationToken)
    {
        var inFence = false;

        foreach (var raw in markdown.Split('\n'))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = raw.TrimEnd('\r');
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (trimmed.Length == 0)
            {
                continue;
            }

            // Inside a fence the line is code: kept verbatim in a monospaced run rather than
            // interpreted, because the point of a fence is that its contents are not markup.
            if (inFence)
            {
                body.Append(Paragraph(line, monospace: true));
                continue;
            }

            var headingLevel = HeadingLevelOf(trimmed);

            if (headingLevel is { } level)
            {
                body.Append(Heading(trimmed[level..].Trim(), (uint)Math.Min(level + 1, 9)));
                continue;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal) ||
                trimmed.StartsWith("* ", StringComparison.Ordinal) ||
                trimmed.StartsWith("+ ", StringComparison.Ordinal))
            {
                body.Append(Bullet(StripInline(trimmed[2..])));
                continue;
            }

            body.Append(Paragraph(StripInline(line)));
        }
    }

    /// <summary>
    /// Counts the leading hashes on a line, or <see langword="null"/> when it is not a heading.
    /// <para>
    /// Bounded at six because Markdown does: a line of seven hashes is not a seventh-level
    /// heading, it is text that happens to start with hashes.
    /// </para>
    /// </summary>
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

    private static Paragraph Heading(string text, uint level)
    {
        var run = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });

        return new Paragraph(
            new ParagraphProperties(new ParagraphStyleId { Val = $"Heading{level.ToString(System.Globalization.CultureInfo.InvariantCulture)}" }),
            run);
    }

    private static Paragraph Bullet(string text) =>
        new(
            new ParagraphProperties(
                new ParagraphStyleId { Val = "ListParagraph" },
                new NumberingProperties(
                    new NumberingLevelReference { Val = 0 },
                    new NumberingId { Val = 1 })),
            new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));

    private static Paragraph Paragraph(
        string text,
        bool italic = false,
        bool monospace = false)
    {
        var properties = new RunProperties();

        if (italic)
        {
            properties.Append(new Italic());
        }

        if (monospace)
        {
            properties.Append(
                new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" },
                new FontSize { Val = "20" });
        }

        var run = new Run();

        if (properties.HasChildren)
        {
            run.Append(properties);
        }

        run.Append(new Text(text) { Space = SpaceProcessingModeValues.Preserve });

        return new Paragraph(run);
    }

    /// <summary>
    /// Removes the inline Markdown markers from a line, keeping the words.
    /// <para>
    /// Deliberately not handling links fully: a link becomes its label followed by its target in
    /// brackets, which reads correctly and never loses the address. Dropping the brackets instead
    /// would lose the address, which in a report about deployment architecture is usually the
    /// most important word on the line.
    /// </para>
    /// </summary>
    private static string StripInline(string line)
    {
        var builder = new System.Text.StringBuilder(line.Length);

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
}