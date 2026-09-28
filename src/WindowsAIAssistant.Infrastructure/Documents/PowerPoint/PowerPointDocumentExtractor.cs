using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.Documents;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models.Documents;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.Documents.PowerPoint;

/// <summary>
/// Reads a presentation, one section per slide.
/// <para>
/// Slides are kept separate because a slide is a self-contained argument: its title and its
/// text are meant to be read together, and flattening a deck into a single run of text loses
/// exactly the thing a person would use to find the slide again. Speaker notes are included and
/// labelled, since they are often where the real content is, and they are the part a slide's
/// visible text omits.
/// </para>
/// <para>
/// Animations, transitions, and images are left alone. The package is opened read-only, and no
/// media part is opened at all.
/// </para>
/// </summary>
public sealed class PowerPointDocumentExtractor : IDocumentExtractor
{
    private readonly IOptionsMonitor<DocumentOptions> _options;
    private readonly ILogger<PowerPointDocumentExtractor> _logger;

    public PowerPointDocumentExtractor(
        IOptionsMonitor<DocumentOptions> options,
        ILogger<PowerPointDocumentExtractor> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public DocumentFileType FileType => DocumentFileType.PowerPoint;

    /// <inheritdoc />
    public bool CanExtract(string extension) =>
        extension.Equals(".pptx", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task<DocumentContent> ExtractAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var document = PresentationDocument.Open(filePath, isEditable: false);
        var presentation = document.PresentationPart?.Presentation
            ?? throw new DocumentException(
                "This presentation could not be read because it has no presentation content.",
                ErrorCodes.DocumentExtractionFailed);

        var slideIds = presentation.SlideIdList?.Elements<SlideId>().ToList() ?? [];
        var limit = _options.CurrentValue.MaximumExtractedCharacters;
        var sections = new List<DocumentSection>();
        var warnings = new List<DocumentWarning>();
        var characters = 0;
        var truncated = false;
        var hasImages = false;
        var hasEmbedded = false;

        for (var index = 0; index < slideIds.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var number = index + 1;
            var relationshipId = slideIds[index].RelationshipId?.Value;
            if (relationshipId is null)
            {
                continue;
            }

            var slidePart = document.PresentationPart.GetPartById(relationshipId) as SlidePart;
            if (slidePart is null)
            {
                continue;
            }

            var (title, body) = ReadSlide(slidePart);
            hasImages |= slidePart.ImageParts.Any();
            hasEmbedded |= slidePart.EmbeddedObjectParts.Any()
                || slidePart.EmbeddedPackageParts.Any();

            var rendered = RenderSlide(number, title, body, ReadNotes(slidePart));
            if (characters + rendered.Length > limit)
            {
                truncated = true;
                break;
            }

            characters += rendered.Length;
            sections.Add(DocumentSection.Create(
                index,
                DocumentSectionKind.Slide,
                $"Slide {number}",
                $"slide {number}",
                rendered));
        }

        if (truncated)
        {
            warnings.Add(DocumentWarningMessages.Create(DocumentWarningKind.ContentTruncated));
        }

        if (hasImages)
        {
            warnings.Add(DocumentWarningMessages.Create(DocumentWarningKind.ImagesNotAnalyzed));
        }

        if (hasEmbedded)
        {
            warnings.Add(DocumentWarningMessages.Create(DocumentWarningKind.EmbeddedContentSkipped));
        }

        _logger.LogInformation(
            "Presentation read. Slides extracted: {SlideCount}. Characters extracted: {CharacterCount}.",
            sections.Count,
            characters);

        var fileName = Path.GetFileName(filePath);
        var properties = document.PackageProperties;

        return Task.FromResult(DocumentContent.Create(
            fileName,
            DocumentFileType.PowerPoint,
            new DocumentMetadata
            {
                FileName = fileName,
                Extension = Path.GetExtension(filePath),
                Title = Blank(properties.Title),
                Author = Blank(properties.Creator),
                SlideCount = slideIds.Count,
            },
            sections,
            warnings));
    }

    /// <summary>
    /// Renders a slide with its title first and its text beneath, so a slide reads the way it
    /// looks. The title is what a reader will search for, and a deck where the titles are
    /// buried in the body text is much harder to answer questions about.
    /// </summary>
    private static string RenderSlide(int number, string? title, IReadOnlyList<string> body, string? notes)
    {
        var builder = new StringBuilder();
        builder.Append("Slide ").Append(number).Append('\n');

        if (!string.IsNullOrWhiteSpace(title))
        {
            builder.Append("Title: ").Append(title.Trim()).Append('\n');
        }

        foreach (var line in body)
        {
            builder.Append(line).Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(notes))
        {
            builder.Append("Speaker notes: ").Append(notes.Trim()).Append('\n');
        }

        return builder.ToString().TrimEnd();
    }

    private static (string? Title, List<string> Body) ReadSlide(SlidePart slidePart)
    {
        var slide = slidePart.Slide;
        if (slide?.CommonSlideData?.ShapeTree is null)
        {
            return (null, []);
        }

        string? title = null;
        var body = new List<string>();

        foreach (var shape in slide.CommonSlideData.ShapeTree.Elements<Shape>())
        {
            var text = ReadShapeText(shape);

            // An empty shape is a placeholder the author never filled in, which is normal and
            // not worth reporting.
            if (text.Length == 0)
            {
                continue;
            }

            if (title is null && IsTitlePlaceholder(shape))
            {
                title = text;
                continue;
            }

            body.Add(text);
        }

        // A deck made entirely of text boxes has no title placeholder. The first line of the
        // slide is then its title, which is how the slide reads on screen too.
        if (title is null && body.Count > 0)
        {
            title = body[0];
            body.RemoveAt(0);
        }

        return (title, body);
    }

    private static bool IsTitlePlaceholder(Shape shape)
    {
        var type = ReadPlaceholderType(shape);
        return type is "title" or "ctrTitle";
    }

    /// <summary>
    /// Reads a shape's placeholder kind from its <c>ph</c> element.
    /// <para>
    /// The element is read by name and attribute rather than through a strongly typed property
    /// because the placeholder types are shared between the drawing, presentation, and
    /// spreadsheet schemas, and matching on the attribute is the one form that means the same
    /// thing in each. A shape that is not a placeholder has no such element, which is the
    /// ordinary case for a text box or a picture.
    /// </para>
    /// </summary>
    private static string? ReadPlaceholderType(Shape shape)
    {
        var properties = shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties;
        if (properties is null)
        {
            return null;
        }

        foreach (var child in properties.ChildElements)
        {
            if (string.Equals(child.LocalName, "ph", StringComparison.Ordinal))
            {
                return child.GetAttributes()
                    .FirstOrDefault(attribute =>
                        string.Equals(attribute.LocalName, "type", StringComparison.Ordinal))
                    .Value;
            }
        }

        return null;
    }

    private static string ReadShapeText(Shape shape)
    {
        var body = shape.TextBody;
        if (body is null)
        {
            return string.Empty;
        }

        var lines = new List<string>();

        foreach (var paragraph in body.Elements<DocumentFormat.OpenXml.Drawing.Paragraph>())
        {
            var text = string.Concat(
                paragraph.Descendants<DocumentFormat.OpenXml.Drawing.Text>()
                    .Select(run => run.Text));

            if (!string.IsNullOrWhiteSpace(text))
            {
                lines.Add(text.Trim());
            }
        }

        return string.Join("\n", lines);
    }

    private static string? ReadNotes(SlidePart slidePart)
    {
        var notes = slidePart.NotesSlidePart?.NotesSlide;
        if (notes?.CommonSlideData?.ShapeTree is null)
        {
            return null;
        }

        var lines = new List<string>();

        foreach (var shape in notes.CommonSlideData.ShapeTree.Elements<Shape>())
        {
            var type = ReadPlaceholderType(shape);

            // The notes part carries the slide's own text in a placeholder as well as the
            // speaker's notes, and repeating the slide here would double every slide's text
            // in the document.
            if (type is "sldImg" or "sldNum" or "hdr" or "ftr" or "dt")
            {
                continue;
            }

            var text = ReadShapeText(shape);
            if (text.Length > 0)
            {
                lines.Add(text);
            }
        }

        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
