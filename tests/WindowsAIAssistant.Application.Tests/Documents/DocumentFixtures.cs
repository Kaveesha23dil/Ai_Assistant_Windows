using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;
using O = DocumentFormat.OpenXml.Office.Drawing;
using P = DocumentFormat.OpenXml.Wordprocessing;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace WindowsAIAssistant.Application.Tests.Documents;

/// <summary>
/// Builds the small files the extractor tests read.
/// <para>
/// Every fixture is generated here rather than checked in. A binary in a repository cannot be
/// reviewed, cannot be diffed when it changes, and is exactly the kind of file that gets opened
/// by a person wondering whether it is safe. Writing the same structure through the same
/// library that will later read it also means a fixture that stops opening shows up as a failure
/// in a test rather than as a mystery.
/// </para>
/// <para>
/// Nothing here is a real document and nothing here is anybody's file. The content is filler
/// written for this purpose, so no test in this project reads a personal file or needs one to
/// exist on the machine it runs on.
/// </para>
/// </summary>
internal static class DocumentFixtures
{
    /// <summary>
    /// A Word document with three headings, paragraphs, and a three-column table.
    /// </summary>
    public static void WriteWordDocument(string path)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        document.PackageProperties.Title = "Quarterly Review";
        document.PackageProperties.Creator = "Test Author";

        var main = document.AddMainDocumentPart();
        main.Document = new P.Document(
            new P.Body(
                Heading("Heading1", "Quarterly Review"),
                Text("The project deadline is October 15."),
                Text("The budget was fixed at the start of the year."),
                Heading("Heading1", "Risks"),
                Text("Delivery depends on a single supplier."),
                BuildTable(),
                Heading("Heading1", "Next Steps")));
    }

    private static P.Paragraph Heading(string style, string text) =>
        new(
            new P.ParagraphProperties(new P.ParagraphStyleId { Val = style }),
            new P.Run(new P.Text(text)));

    private static P.Paragraph Text(string text) => new(new P.Run(new P.Text(text)));

    private static P.Table BuildTable()
    {
        P.TableRow Row(params string[] cells) =>
            new(cells.Select(cell => new P.TableCell(Text(cell))));

        return new P.Table(
            Row("Name", "Role", "Status"),
            Row("Ada", "Developer", "Active"),
            Row("Grace", "Reviewer", "Inactive"));
    }

    /// <summary>
    /// A three-slide presentation, the second of which has speaker notes.
    /// </summary>
    public static void WritePresentation(string path)
    {
        using var document = PresentationDocument.Create(path, PresentationDocumentType.Presentation);
        document.PackageProperties.Title = "Project Overview";
        document.PackageProperties.Creator = "Test Author";

        var presentationPart = document.AddPresentationPart();
        var slideIds = new SlideIdList();
        presentationPart.Presentation = new Presentation(slideIds);

        AddSlide(presentationPart, slideIds, "Project Overview", ["Goal", "Schedule"]);
        AddSlide(presentationPart, slideIds, "Timeline", ["October 15 launch"], "Emphasise the launch date.");
        AddSlide(presentationPart, slideIds, "Budget", ["Fixed for the year"]);
    }

    private static void AddSlide(
        PresentationPart presentationPart,
        SlideIdList slideIds,
        string title,
        IReadOnlyList<string> lines,
        string? notes = null)
    {
        var slidePart = presentationPart.AddNewPart<SlidePart>();

        var shapes = new ShapeTree(SlideTextShape(title, PlaceholderValues.Title));

        foreach (var line in lines)
        {
            shapes.Append(SlideTextShape(line, PlaceholderValues.Body));
        }

        slidePart.Slide = new Slide(new CommonSlideData(shapes));

        if (notes is not null)
        {
            var notesPart = slidePart.AddNewPart<NotesSlidePart>();
            notesPart.NotesSlide = new NotesSlide(
                new CommonSlideData(new ShapeTree(TextShape(notes))));
        }

        slideIds.Append(new SlideId
        {
            Id = (uint)slideIds.ChildElements.Count + 1,
            RelationshipId = presentationPart.GetIdOfPart(slidePart),
        });
    }

    private static Shape SlideTextShape(string text, PlaceholderValues type) =>
        Shape(text, new PlaceholderShape { Type = type });

    private static Shape TextShape(string text) => Shape(text, null);

    /// <summary>
    /// A shape carrying text. The parts are assembled by hand because this is the shape PowerPoint
    /// itself writes, and the reader takes the shape tree apart directly rather than through a
    /// layout API. The placeholder, when there is one, belongs to the non-visual properties
    /// rather than to the shape's own name, which is where PowerPoint puts it.
    /// </summary>
    private static Shape Shape(string text, PlaceholderShape? placeholder) =>
        new(
            new NonVisualShapeProperties(
                new NonVisualDrawingProperties(),
                new O.NonVisualDrawingShapeProperties(),
                placeholder is null
                    ? new ApplicationNonVisualDrawingProperties()
                    : new ApplicationNonVisualDrawingProperties { PlaceholderShape = placeholder }),
            new A.Transform2D(
                new A.Offset { X = 0L, Y = 0L },
                new A.Extents { Cx = 0L, Cy = 0L }),
            new TextBody(
                new A.BodyProperties(),
                new A.ListStyle(),
                new A.Paragraph(new A.Run(new A.Text(text)))));

    /// <summary>
    /// A workbook with two worksheets: the first a small table with a date and a number, the
    /// second a single value.
    /// </summary>
    public static void WriteWorkbook(string path)
    {
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        document.PackageProperties.Title = "Budget";
        document.PackageProperties.Creator = "Test Author";

        var workbookPart = document.AddWorkbookPart();
        var sheets = new S.Sheets();
        workbookPart.Workbook = new S.Workbook(sheets);
        AddStyles(workbookPart);

        // Shared strings, so the reader is exercised against the indirection a real workbook
        // uses rather than only against inline text.
        var strings = new List<string>();
        AddSheet(workbookPart, sheets, "Summary", sheetData =>
        {
            sheetData.Append(Row(
                Cell("Item", strings),
                Cell("Cost", strings),
                Cell("Due", strings)));

            sheetData.Append(Row(
                Cell("Licences", strings),
                Number("1200"),
                DateCell("45833")));

            sheetData.Append(Row(
                Cell("Travel", strings),
                Number("480"),
                DateCell("45865")));
        });

        AddSheet(workbookPart, sheets, "Notes", sheetData =>
            sheetData.Append(Row(Cell("The budget is fixed.", strings))));

        var table = workbookPart.AddNewPart<SharedStringTablePart>();
        var items = new S.SharedStringTable();

        foreach (var value in strings)
        {
            items.Append(new S.SharedStringItem(new S.Text(value)));
        }

        table.SharedStringTable = items;
    }

    /// <summary>
    /// Adds enough cell formats that style index 14 — the built-in date format the reader looks
    /// for — actually exists. A workbook claiming an index it does not have is one Excel would
    /// repair, and a fixture that is not a real file is a fixture that proves nothing.
    /// </summary>
    private static void AddStyles(WorkbookPart workbookPart)
    {
        var formats = new S.CellFormats();

        for (var index = 0; index < 15; index++)
        {
            formats.Append(new S.CellFormat());
        }

        var part = workbookPart.AddNewPart<WorkbookStylesPart>();
        part.Stylesheet = new S.Stylesheet(formats);
    }

    private static void AddSheet(
        WorkbookPart workbookPart,
        S.Sheets sheets,
        string name,
        Action<S.SheetData> fill)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new S.SheetData();
        fill(sheetData);
        worksheetPart.Worksheet = new S.Worksheet(sheetData);

        sheets.Append(new S.Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = (uint)sheets.ChildElements.Count + 1,
            Name = name,
        });
    }

    private static S.Row Row(params S.Cell[] cells) => new(cells);

    private static S.Cell Cell(string value, List<string> strings)
    {
        strings.Add(value);
        return new S.Cell
        {
            DataType = S.CellValues.SharedString,
            CellValue = new S.CellValue((strings.Count - 1).ToString(CultureInfo.InvariantCulture)),
        };
    }

    private static S.Cell Number(string value) => new() { CellValue = new S.CellValue(value) };

    private static S.Cell DateCell(string serial) => new() { StyleIndex = 14U, CellValue = new S.CellValue(serial) };

    /// <summary>
    /// A PDF with the given number of pages, each carrying the given lines of text.
    /// <para>
    /// Written out by hand because this is the one format here that is not a package: a PDF is a
    /// numbered list of objects and a cross-reference table pointing at their byte offsets. The
    /// content stream is left uncompressed so that the text in the file is the text the reader
    /// will find.
    /// </para>
    /// </summary>
    public static void WritePdf(string path, IReadOnlyList<IReadOnlyList<string>> pages) =>
        WritePdfFile(path, pages, passwordRequired: false);

    /// <summary>
    /// A PDF whose pages carry a filled rectangle and no text at all, which is what a page
    /// scanned as an image amounts to as far as text extraction is concerned.
    /// </summary>
    public static void WriteImageOnlyPdf(string path, int pageCount) =>
        WritePdfFile(
            path,
            Enumerable.Repeat<IReadOnlyList<string>>([], pageCount).ToList(),
            passwordRequired: false,
            drawInsteadOfText: true);

    /// <summary>
    /// A PDF carrying an encryption dictionary whose user password is not the empty one, so
    /// opening it requires a password this build does not have and will not try to guess.
    /// </summary>
    public static void WritePasswordProtectedPdf(string path) =>
        WritePdfFile(path, [["Protected text."]], passwordRequired: true);

    private static void WritePdfFile(
        string path,
        IReadOnlyList<IReadOnlyList<string>> pages,
        bool passwordRequired,
        bool drawInsteadOfText = false)
    {
        // Object 1 is the catalog, 2 the page tree, 3 the font, and then a page and a content
        // stream for each page. The encryption dictionary, when present, follows them.
        var pageObjectNumbers = pages.Select((_, index) => 4 + (2 * index)).ToList();
        var encryptionObjectNumber = 4 + (2 * pages.Count);

        var objects = new List<string>(4 + (2 * pages.Count))
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(' ', pageObjectNumbers.Select(n => $"{n} 0 R"))}] /Count {pages.Count} >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        };

        for (var index = 0; index < pages.Count; index++)
        {
            var contentNumber = pageObjectNumbers[index] + 1;
            var stream = drawInsteadOfText
                ? "0.6 g\n72 500 468 200 re\nf\n"
                : TextStream(pages[index]);

            objects.Add(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " +
                $"/Resources << /Font << /F1 3 0 R >> >> /Contents {contentNumber} 0 R >>");

            objects.Add(
                $"<< /Length {Encoding.Latin1.GetByteCount(stream)} >>\nstream\n{stream}endstream");
        }

        if (passwordRequired)
        {
            // Revision 2, forty-bit. The owner and user entries below are deliberately not the
            // values an empty password produces, so a reader has to refuse rather than open it.
            objects.Add(
                "<< /Filter /Standard /V 1 /R 2 " +
                "/O <0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF> " +
                "/U <FEDCBA9876543210FEDCBA9876543210FEDCBA9876543210FEDCBA9876543210> /P -1 >>");
        }

        var builder = new StringBuilder();
        builder.Append("%PDF-1.4\n");

        // The high bytes mark the file as binary for anything that would otherwise treat it as
        // text. They occupy four bytes, which the offsets below account for.
        builder.Append("%\u00E2\u00E3\u00CF\u00D3\n");

        var offsets = new List<int>(objects.Count);

        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(Latin1Length(builder));
            builder.Append(index + 1).Append(" 0 obj\n").Append(objects[index]).Append("\nendobj\n");
        }

        var startxref = Latin1Length(builder);

        builder.Append("xref\n0 ").Append(objects.Count + 1).Append('\n');
        builder.Append("0000000000 65535 f \n");

        foreach (var offset in offsets)
        {
            builder.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        builder.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R");

        if (passwordRequired)
        {
            builder.Append(" /Encrypt ").Append(encryptionObjectNumber).Append(" 0 R");
        }

        builder.Append(" >>\nstartxref\n").Append(startxref).Append("\n%%EOF\n");

        File.WriteAllBytes(path, Encoding.Latin1.GetBytes(builder.ToString()));
    }

    /// <summary>
    /// The number of bytes the text will occupy once written. Anything outside Latin-1 becomes
    /// a single replacement character, so the count is right without holding the whole file
    /// twice.
    /// </summary>
    private static int Latin1Length(StringBuilder builder) =>
        Encoding.Latin1.GetByteCount(builder.ToString());

    private static string TextStream(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.Append("BT\n/F1 12 Tf\n72 720 Td\n14 TL\n");

        foreach (var line in lines)
        {
            builder.Append('(').Append(Escape(line)).Append(") Tj\nT*\n");
        }

        builder.Append("ET\n");
        return builder.ToString();
    }

    /// <summary>
    /// Escapes the two characters that mean something inside a PDF text string, and drops the
    /// ones a Latin-1 encoding cannot carry.
    /// </summary>
    private static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (character is '(' or ')' or '\\')
            {
                builder.Append('\\').Append(character);
            }
            else if (character <= 0xFF)
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
