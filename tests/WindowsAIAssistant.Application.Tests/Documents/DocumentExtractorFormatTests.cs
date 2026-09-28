using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Infrastructure.Configuration.Options;
using WindowsAIAssistant.Infrastructure.Documents;
using WindowsAIAssistant.Infrastructure.Documents.Excel;
using WindowsAIAssistant.Infrastructure.Documents.Pdf;
using WindowsAIAssistant.Infrastructure.Documents.PowerPoint;
using WindowsAIAssistant.Infrastructure.Documents.Text;
using WindowsAIAssistant.Infrastructure.Documents.Word;

namespace WindowsAIAssistant.Application.Tests.Documents;

/// <summary>
/// Reads real files in every supported format through the same reader the application uses.
/// <para>
/// These are the tests that would notice a file format being read wrongly in a way a summary
/// would then quietly pass on: a paragraph skipped, a slide merged into its neighbour, a column
/// dropped, a page number that no longer matches the page. Each one writes a file it knows the
/// contents of, and checks the reader got them back.
/// </para>
/// </summary>
public sealed class DocumentExtractorFormatTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "waip-format-tests-" + Guid.NewGuid().ToString("N"));

    public DocumentExtractorFormatTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task AWordDocumentComesBackWithItsHeadingsParagraphsAndTable()
    {
        var path = Path.Combine(_directory, "review.docx");
        DocumentFixtures.WriteWordDocument(path);

        var content = await Reader().ReadAsync(path);

        Assert.Equal(DocumentFileType.Word, content.FileType);
        Assert.Equal("Quarterly Review", content.Metadata.Title);
        Assert.Equal("Test Author", content.Metadata.Author);

        // Headings are the document's own structure, and a summary that could not say which
        // section something came from would be much harder to check against the file.
        Assert.Contains(content.Sections, section => section.Name == "Risks");

        var text = content.RawText;
        Assert.Contains("The project deadline is October 15.", text, StringComparison.Ordinal);

        // A table rendered as rows rather than prose: the cells separated, the row's own words
        // intact, and the row order kept.
        Assert.Contains("Ada | Developer | Active", text, StringComparison.Ordinal);
        Assert.Contains("Grace | Reviewer | Inactive", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWordTableIsNotReportedAsProse()
    {
        var path = Path.Combine(_directory, "review.docx");
        DocumentFixtures.WriteWordDocument(path);

        var content = await Reader().ReadAsync(path);

        // The table is its own section, because a table buried in a run of paragraphs is how a
        // row of figures gets read as a sentence.
        var table = Assert.Single(content.Sections, section => section.Kind == DocumentSectionKind.Table);
        Assert.Equal("Name | Role | Status", table.Text.Split('\n')[0]);
    }

    [Fact]
    public async Task APresentationKeepsItsSlidesApart()
    {
        var path = Path.Combine(_directory, "deck.pptx");
        DocumentFixtures.WritePresentation(path);

        var content = await Reader().ReadAsync(path);

        Assert.Equal(DocumentFileType.PowerPoint, content.FileType);
        Assert.Equal(3, content.Metadata.SlideCount);
        Assert.Equal(3, content.Sections.Count);
        Assert.Equal(
            ["Slide 1", "Slide 2", "Slide 3"],
            content.Sections.Select(section => section.Name));

        // A slide reads the way it looks: number, then title, then the text under it.
        Assert.StartsWith("Slide 1\nTitle: Project Overview\n", content.Sections[0].Text, StringComparison.Ordinal);
        Assert.Contains("Goal", content.Sections[0].Text, StringComparison.Ordinal);
        Assert.Contains("Budget", content.Sections[2].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SpeakerNotesAreReadAndLabelled()
    {
        var path = Path.Combine(_directory, "deck.pptx");
        DocumentFixtures.WritePresentation(path);

        var content = await Reader().ReadAsync(path);

        // Notes are often the part of a slide that says what the point of it is.
        Assert.Contains("Speaker notes: Emphasise the launch date.", content.Sections[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWorkbookComesBackWithItsSheetNamesAndValues()
    {
        var path = Path.Combine(_directory, "budget.xlsx");
        DocumentFixtures.WriteWorkbook(path);

        var content = await Reader().ReadAsync(path);

        Assert.Equal(DocumentFileType.Excel, content.FileType);
        Assert.Equal(2, content.Metadata.SheetCount);
        Assert.Equal(["Summary", "Notes"], content.Sections.Select(section => section.Name));

        // The citation names the sheet, because "the number is on page 4" is no use for a
        // workbook and "it is in the Summary sheet" is.
        Assert.Equal("Sheet \"Summary\"", content.Sections[0].Reference);

        var summary = content.Sections[0].Text;
        Assert.Contains("Licences", summary, StringComparison.Ordinal);
        Assert.Contains("1200", summary, StringComparison.Ordinal);
        Assert.Contains("The budget is fixed.", content.Sections[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADateCellIsReportedAsADateRatherThanAsANumber()
    {
        var path = Path.Combine(_directory, "budget.xlsx");
        DocumentFixtures.WriteWorkbook(path);

        var content = await Reader().ReadAsync(path);

        // Serial 45833 is a number with no meaning outside a spreadsheet. Reported as itself, a
        // summary of a schedule would be a summary of nonsense.
        Assert.Contains("2025-06-25", content.Sections[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWorkbookIsCutOffAtTheCellLimitAndSaysSo()
    {
        var options = new DocumentOptions { MaximumCells = 4, MaximumRowsPerSheet = 3, MaximumColumnsPerSheet = 2 };
        var path = Path.Combine(_directory, "budget.xlsx");
        DocumentFixtures.WriteWorkbook(path);

        var content = await Reader(options).ReadAsync(path);

        // A spreadsheet of a hundred thousand rows is the case the limits exist for. Being cut
        // short is fine; not saying so is not, because a summary of the first four cells reads
        // as a summary of the workbook.
        Assert.Contains(content.ExtractionWarnings, warning => warning.Kind == DocumentWarningKind.SpreadsheetTruncated);
    }

    [Fact]
    public async Task AWideWorksheetIsCutOffAtTheColumnLimit()
    {
        var options = new DocumentOptions { MaximumColumnsPerSheet = 1 };
        var path = Path.Combine(_directory, "budget.xlsx");
        DocumentFixtures.WriteWorkbook(path);

        var content = await Reader(options).ReadAsync(path);

        var summary = content.Sections[0].Text;
        Assert.Contains("Item", summary, StringComparison.Ordinal);

        // The limit is on columns, so it is the second column onward that goes. Every row still
        // appears, which is the point: a schedule read one column wide is still every row of it.
        Assert.DoesNotContain("Cost", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("1200", summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APdfComesBackOneSectionPerPage()
    {
        var path = Path.Combine(_directory, "paper.pdf");
        DocumentFixtures.WritePdf(path, [["First page text."], ["Second page text."]]);

        var content = await Reader().ReadAsync(path);

        Assert.Equal(DocumentFileType.Pdf, content.FileType);
        Assert.Equal(2, content.Metadata.PageCount);
        Assert.Equal(2, content.Sections.Count);
        Assert.Equal(
            [DocumentSectionKind.Page, DocumentSectionKind.Page],
            content.Sections.Select(section => section.Kind));

        // A page number is what an answer can be checked against, so it has to be the page's
        // real number and not its position in the list of pages that had text.
        Assert.Equal("Page 1", content.Sections[0].Name);
        Assert.Equal("page 1", content.Sections[0].Reference);
        Assert.Equal("page 2", content.Sections[1].Reference);
        Assert.Contains("First page text.", content.Sections[0].Text, StringComparison.Ordinal);
        Assert.Contains("Second page text.", content.Sections[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnImageOnlyPdfIsReportedAsNeedingOcrRatherThanAsEmpty()
    {
        var path = Path.Combine(_directory, "scan.pdf");
        DocumentFixtures.WriteImageOnlyPdf(path, pageCount: 2);

        var error = await ThrowsAsync(() => Reader().ReadAsync(path));

        // The distinction is the whole point. An empty document has nothing in it; a scanned one
        // has everything in it as pictures, and answering questions about the second from the
        // first would be answering about a page of nothing.
        Assert.Equal(ErrorCodes.DocumentOcrRequired, error.ErrorCode);
        Assert.Contains("scanned", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task APasswordProtectedPdfIsReportedRatherThanGuessedAt()
    {
        var path = Path.Combine(_directory, "locked.pdf");
        DocumentFixtures.WritePasswordProtectedPdf(path);

        var error = await ThrowsAsync(() => Reader().ReadAsync(path));

        Assert.Equal(ErrorCodes.DocumentPasswordRequired, error.ErrorCode);
    }

    [Fact]
    public async Task EveryFactoryRoutesEachFormatToItsOwnReader()
    {
        var factory = Factory(new DocumentOptions());
        var detector = new DocumentTypeDetector();

        foreach (var (fileName, expected) in new (string, Type)[]
        {
            ("paper.pdf", typeof(PdfDocumentExtractor)),
            ("review.docx", typeof(WordDocumentExtractor)),
            ("deck.pptx", typeof(PowerPointDocumentExtractor)),
            ("budget.xlsx", typeof(ExcelDocumentExtractor)),
            ("notes.txt", typeof(PlainTextDocumentExtractor)),
            ("notes.md", typeof(PlainTextDocumentExtractor)),
            ("data.csv", typeof(PlainTextDocumentExtractor)),
            ("app.cs", typeof(PlainTextDocumentExtractor)),
            ("build.log", typeof(PlainTextDocumentExtractor)),
        })
        {
            var type = factory.GetExtractor(detector.Detect(fileName)).GetType();

            Assert.True(expected.IsAssignableFrom(type), $"{fileName} went to {type?.Name}");
        }
    }

    [Fact]
    public async Task AFileThatIsNotOneOfTheSupportedOnesIsRefusedByName()
    {
        var path = Path.Combine(_directory, "model.xyz");
        File.WriteAllText(path, "some bytes");

        var error = await ThrowsAsync(() => Reader().ReadAsync(path));

        Assert.Equal(ErrorCodes.DocumentFormatUnsupported, error.ErrorCode);
        Assert.Contains("model.xyz", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFileWhoseBytesContradictItsExtensionIsRefused()
    {
        // A PDF named .docx. Two formats disagree about what the file is, and picking either
        // would be a guess: the Word reader would report a damaged package, and reading it as a
        // PDF would be reading something the person did not name.
        var path = Path.Combine(_directory, "paper.docx");
        DocumentFixtures.WritePdf(path, [["Not a Word document."]]);

        var error = await ThrowsAsync(() => Reader().ReadAsync(path));

        Assert.Equal(ErrorCodes.DocumentFormatUnsupported, error.ErrorCode);
    }

    [Fact]
    public async Task ATextFileIsReadEvenWhenItBeginsWithAnotherFormatsMarker()
    {
        // The other direction, which is a different decision. A text file may begin with any
        // characters at all, so the leading bytes prove nothing when the extension already says
        // text — and refusing it would mean a person could not open a note that quotes a PDF
        // header.
        var path = Path.Combine(_directory, "notes.txt");
        File.WriteAllText(path, "%PDF-1.7 is a string people paste into notes");

        var content = await Reader().ReadAsync(path);

        Assert.Equal(DocumentFileType.PlainText, content.FileType);
        Assert.Contains("%PDF", content.RawText, StringComparison.Ordinal);
    }

    private DocumentReader Reader(DocumentOptions? options = null) =>
        new(
            new DocumentTypeDetector(),
            Factory(options ?? new DocumentOptions()),
            new StaticOptions(options ?? new DocumentOptions()),
            NullLogger<DocumentReader>.Instance);

    private static DocumentExtractorFactory Factory(DocumentOptions options)
    {
        IOptionsMonitor<DocumentOptions> monitor = new StaticOptions(options);

        return new DocumentExtractorFactory(
            [
                new PlainTextDocumentExtractor(monitor, NullLogger<PlainTextDocumentExtractor>.Instance),
                new WordDocumentExtractor(monitor, NullLogger<WordDocumentExtractor>.Instance),
                new PowerPointDocumentExtractor(monitor, NullLogger<PowerPointDocumentExtractor>.Instance),
                new ExcelDocumentExtractor(monitor, NullLogger<ExcelDocumentExtractor>.Instance),
                new PdfDocumentExtractor(monitor, NullLogger<PdfDocumentExtractor>.Instance),
            ],
            new DocumentTypeDetector());
    }

    private static async Task<DocumentException> ThrowsAsync(Func<Task> work) =>
        await Assert.ThrowsAsync<DocumentException>(work);

    private sealed class StaticOptions(DocumentOptions value) : IOptionsMonitor<DocumentOptions>
    {
        public DocumentOptions CurrentValue => value;

        public DocumentOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<DocumentOptions, string?> listener) => null;
    }
}
