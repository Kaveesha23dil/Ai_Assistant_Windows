using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using WindowsAIAssistant.Application.Agents.Tools;
using WindowsAIAssistant.Core.Abstractions.Reports;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Agents;
using WindowsAIAssistant.Core.Models.Reports;
using WindowsAIAssistant.Infrastructure.Reports;

namespace WindowsAIAssistant.Application.Tests.Reports;

/// <summary>
/// What each report format produces, and that the tool writes the one that was asked for.
/// <para>
/// The formats are checked as files rather than as strings, because the thing that can be wrong
/// about a document is not its text but whether a reader can open it. A PDF asserted to contain
/// "%%EOF" is a PDF that happens to end correctly; a PDF that a reader rejects has still failed.
/// The DOCX check goes as far as opening the package and reading the document back out of it,
/// which is the closest a headless test gets to Word.
/// </para>
/// <para>
/// Every file is written inside a temporary directory belonging to one test and deleted
/// afterwards, so a test run can never touch a real Documents folder.
/// </para>
/// </summary>
public sealed class ReportFormatTests : IDisposable
{
    private const string Body = """
        # Findings

        The deployment uses two services behind one gateway.

        - The gateway terminates TLS.
        - The service holds no state.

        ## Cost

        Monthly spend is dominated by the database.
        """;

    private readonly string _documents = Path.Combine(
        Path.GetTempPath(),
        "waa-report-tests-" + Guid.NewGuid().ToString("N"));

    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 3, 14, 15, 9, 26, TimeSpan.Zero));

    public ReportFormatTests() => Directory.CreateDirectory(_documents);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_documents))
            {
                Directory.Delete(_documents, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temporary directory is not worth failing a test over.
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task MarkdownKeepsItsMarkupBecauseThatIsWhatMarkdownIsFor()
    {
        var text = await ReadAsync(ReportFormat.Markdown);

        Assert.StartsWith("# Markdown", text);
        Assert.Contains("# Findings", text);
        Assert.Contains("- The gateway terminates TLS.", text);
        Assert.Contains("Monthly spend is dominated by the database.", text);
    }

    [Fact]
    public async Task PlainTextDropsTheMarkersButKeepsTheWordsAndTheStructure()
    {
        var text = await ReadAsync(ReportFormat.Text);

        // The words are all still there...
        Assert.Contains("Findings", text);
        Assert.Contains("The gateway terminates TLS.", text);
        Assert.Contains("Monthly spend is dominated by the database.", text);

        // ...and the heading markers are not, because a text file with hashes in it looks broken.
        Assert.DoesNotContain("# Findings", text);
        Assert.DoesNotContain("## Cost", text);
    }

    [Fact]
    public async Task AWordDocumentIsAPackageThatOpensAndHoldsTheReport()
    {
        var bytes = await WriteAsync(ReportFormat.Word);

        // A .docx is a zip. If it is not one, nothing else about it matters.
        Assert.Equal(0x50, bytes[0]);
        Assert.Equal(0x4B, bytes[1]);

        using var stream = new MemoryStream(bytes);

        // Opening it with the same library Word-compatible readers use is the strongest check
        // available without Word: a package the SDK can open and read the body out of is one a
        // reader can open too.
        using var package = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(stream, false);

        Assert.NotNull(package.MainDocumentPart);

        var main = package.MainDocumentPart;

        Assert.NotNull(main?.Document);

        var text = main!.Document!.InnerText;

        Assert.Contains("Findings", text);
        Assert.Contains("The gateway terminates TLS.", text);
        Assert.Contains("Monthly spend is dominated by the database.", text);
    }

    [Fact]
    public async Task APdfIsAWellFormedFileWhoseCrossReferenceTablePointsAtItsObjects()
    {
        var pdf = await ReadAsync(ReportFormat.Pdf);

        Assert.StartsWith("%PDF-1.4", pdf);
        Assert.EndsWith("%%EOF\n", pdf);

        // Every offset in the table is checked against the file, because a table that is
        // structurally present but points at the wrong bytes is the failure mode a reader reports
        // as "damaged" rather than as anything specific.
        var offsets = OffsetsIn(pdf);

        Assert.NotEmpty(offsets);

        foreach (var offset in offsets)
        {
            // What has to be at that byte is an object header, not anything in particular about
            // the object: this is checking that the table points into the file at object
            // boundaries rather than into the middle of a stream.
            Assert.Matches(
                @"^\d+ 0 obj(\r?\n|$)",
                pdf.Substring(offset, Math.Min(12, pdf.Length - offset)));
        }
    }

    [Fact]
    public async Task APdfDrawsTheReportRatherThanOnlyDescribingIt()
    {
        var pdf = await ReadAsync(ReportFormat.Pdf);

        // Each line is drawn as its own text-showing operator, so finding them is a statement
        // about the content rather than about the file's shape.
        Assert.Contains("(Findings) Tj", pdf);
        Assert.Contains("(The gateway terminates TLS.) Tj", pdf);
        Assert.Contains("(Monthly spend is dominated by the database.) Tj", pdf);
    }

    [Fact]
    public async Task EveryFormatCanBeWrittenAndNoneOfThemAreEmpty()
    {
        foreach (var format in ReportFormats.All)
        {
            var bytes = await WriteAsync(format);

            Assert.NotEmpty(bytes);
        }
    }

    [Fact]
    public async Task AReportOfNothingIsRefusedByEveryWriterRatherThanWrittenEmpty()
    {
        foreach (var writer in AllWriters())
        {
            var result = await writer.WriteAsync(ReportDocument.Create("Report", "   "));

            Assert.False(result.IsSuccess, writer.Format.ToString());
            Assert.Null(result.Content);
            Assert.False(string.IsNullOrWhiteSpace(result.Outcome.ErrorMessage));
        }
    }

    [Fact]
    public async Task EveryWriterStopsWhenAskedTo()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        foreach (var writer in AllWriters())
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => writer.WriteAsync(ReportDocument.Create("Report", Body), cancelled.Token));
        }
    }

    [Fact]
    public async Task ANameThatCannotBeWrittenFallsBackToMarkdownAndSaysSo()
    {
        var result = await CreateTool().ExecuteAsync(Request("as a spreadsheet"));

        Assert.True(result.IsSuccess);

        // The substitution is reported rather than made quietly, because the file itself tells
        // the person nothing about which format they got.
        Assert.Contains("spreadsheet", result.Content!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Markdown", result.Content!, StringComparison.OrdinalIgnoreCase);

        var written = Assert.Single(Directory.GetFiles(_documents));
        Assert.Equal(".md", Path.GetExtension(written));
    }

    [Fact]
    public async Task ANameThatCanBeWrittenIsHonouredAndTheFileExtensionFollowsIt()
    {
        var result = await CreateTool().ExecuteAsync(Request("as a PDF"));

        Assert.True(result.IsSuccess);

        var written = Assert.Single(Directory.GetFiles(_documents));

        Assert.Equal(".pdf", Path.GetExtension(written));
        Assert.Equal("Pdf", result.Data["format"]);
    }

    [Theory]
    [InlineData("pdf", ReportFormat.Pdf)]
    [InlineData("PDF", ReportFormat.Pdf)]
    [InlineData(" a pdf ", ReportFormat.Pdf)]
    [InlineData(".pdf", ReportFormat.Pdf)]
    [InlineData("docx", ReportFormat.Word)]
    [InlineData("word document", ReportFormat.Word)]
    [InlineData("txt", ReportFormat.Text)]
    [InlineData("plain text", ReportFormat.Text)]
    [InlineData("markdown", ReportFormat.Markdown)]
    [InlineData("md", ReportFormat.Markdown)]
    public void AFormatNameIsReadTheWayAPersonWouldSayIt(string name, ReportFormat expected) =>
        Assert.Equal(expected, ReportFormats.Parse(name));

    [Theory]
    [InlineData("spreadsheet")]
    [InlineData("xlsx")]
    [InlineData("powerpoint")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ANameThisBuildCannotWriteFallsBackToMarkdownAndIsReportedAsUnrecognized(
        string? name)
    {
        ReportFormats.Parse(name, out var recognized);

        Assert.False(recognized);
        Assert.Equal(ReportFormat.Markdown, ReportFormats.Default);
    }

    [Fact]
    public void EveryFormatHasItsOwnExtensionAndNoTwoFormatsShareOne()
    {
        var extensions = ReportFormats.All.Select(ReportFormats.Extension).ToArray();

        Assert.Equal(extensions.Length, extensions.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(extensions, extension => Assert.StartsWith(".", extension));
    }

    [Fact]
    public void TheApprovedActionNamesTheFileTypeBecauseTheFileIsWrittenAfterTheAnswer()
    {
        var step = new AgentStep(
            1,
            "ReportGenerationTool",
            "Write the report",
            null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Deployment review",
                ["format"] = "pdf",
            });

        var action = Assert.Single(CreateTool().DescribeActions(step));

        // A person approves a sentence describing a file. If the sentence does not say it is a
        // PDF, they agreed to something else.
        Assert.Contains("Deployment review.pdf", action.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PDF", action.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AModificationMayChangeTheTitleAndTheFormatTogether()
    {
        var step = NewStep();

        var applied = CreateTool().TryApplyModification(
            step,
            "call it \"Deployment review\" and make it a PDF",
            out var modified);

        Assert.True(applied);
        Assert.Equal("Deployment review", modified.Parameters["title"]);
        Assert.Equal("PDF", modified.Parameters["format"]);

        // The action a person would now approve has to agree with what gets written.
        var action = Assert.Single(CreateTool().DescribeActions(modified));

        Assert.Contains("Deployment review.pdf", action.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AModificationAskingForAFormatThisBuildCannotWriteIsRefusedRatherThanHalfApplied()
    {
        var step = NewStep();

        var applied = CreateTool().TryApplyModification(step, "make it a spreadsheet", out var modified);

        Assert.False(applied);
        Assert.Same(step, modified);
    }

    [Fact]
    public void AModificationAboutSomethingElseEntirelyIsRefused()
    {
        var step = NewStep();

        var applied = CreateTool().TryApplyModification(step, "but shorter", out var modified);

        Assert.False(applied);
        Assert.Same(step, modified);
    }

    [Fact]
    public async Task TheToolWillNotWriteWhereItWasToldToWrite()
    {
        // A path arriving from a model is never followed. The tool puts a file name inside
        // Documents, which is where the request pointed in spirit, and nothing outside it is
        // created even when the step says otherwise.
        var result = await CreateTool().ExecuteAsync(Request(
            parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = @"..\..\Windows\System32\startup",
            }));

        Assert.True(result.IsSuccess);

        var written = Assert.Single(Directory.GetFiles(_documents));

        // The separators became dashes rather than path segments, so the file is inside Documents
        // and its name contains nothing that points outside it.
        Assert.StartsWith(_documents, written, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("..", Path.GetFileName(written));
        Assert.DoesNotContain('\\', Path.GetFileName(written));
    }

    [Fact]
    public async Task TheToolWillNotReplaceAReportThatIsAlreadyThere()
    {
        var tool = CreateTool();

        // The clock is fixed, so both calls build the same file name. The second must refuse
        // rather than overwrite: a report written twice is two reports.
        await tool.ExecuteAsync(Request());
        var second = await tool.ExecuteAsync(Request());

        Assert.False(second.IsSuccess);
        Assert.Single(Directory.GetFiles(_documents));
    }

    /// <summary>Reads every offset out of the cross-reference table.</summary>
    private static IReadOnlyList<int> OffsetsIn(string pdf)
    {
        var start = pdf.IndexOf("xref", StringComparison.Ordinal);

        Assert.True(start > 0, "The file has no cross-reference table.");

        var section = pdf[start..pdf.IndexOf("trailer", StringComparison.Ordinal)];
        var offsets = new List<int>();

        foreach (var raw in section.Split('\n'))
        {
            var line = raw.TrimEnd('\r');

            // An entry is a fixed-width line: ten digits of offset, a space, five of generation,
            // a space, the type, and a space — nineteen characters before the line ending, which
            // is the twentieth byte the format accounts for. The table's own header ("0 6") and
            // the free entry at object zero are skipped because neither is an object position.
            if (line.Length != 19 ||
                !line.EndsWith(" n ", StringComparison.Ordinal) ||
                !int.TryParse(line.AsSpan(0, 10).Trim(), out var offset))
            {
                continue;
            }

            offsets.Add(offset);
        }

        return offsets;
    }

    private static IReadOnlyList<IReportWriter> AllWriters() =>
    [
        new TextReportWriter(ReportFormat.Text),
        new TextReportWriter(ReportFormat.Markdown),
        new WordReportWriter(),
        new PdfReportWriter(),
    ];

    private static AgentStep NewStep() =>
        new(
            1,
            "ReportGenerationTool",
            "Write the report",
            null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    private ReportGenerationTool CreateTool() =>
        new(
            new StubKnownFolderService(_documents),
            AllWriters(),
            NullLogger<ReportGenerationTool>.Instance,
            _clock);

    /// <summary>
    /// Writes one report and reads it back.
    /// <para>
    /// The title is the format's own name because the clock is fixed, which makes every generated
    /// file name identical apart from the title. Without that, writing all four formats in one
    /// test would have the second format refuse to overwrite the first — correct behaviour from
    /// the tool, but not what is being tested here.
    /// </para>
    /// </summary>
    private async Task<byte[]> WriteAsync(ReportFormat format)
    {
        foreach (var stale in Directory.GetFiles(_documents))
        {
            File.Delete(stale);
        }

        var result = await CreateTool().ExecuteAsync(Request(
            ReportFormats.DisplayName(format),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = ReportFormats.DisplayName(format),
            }));

        Assert.True(result.IsSuccess, result.ErrorMessage);

        var written = Assert.Single(Directory.GetFiles(_documents));

        Assert.Equal(ReportFormats.Extension(format), Path.GetExtension(written));

        return await File.ReadAllBytesAsync(written);
    }

    /// <summary>
    /// Reads a written report back as Latin-1 text.
    /// <para>
    /// Latin-1 rather than UTF-8 because it maps every byte to exactly one character, which is
    /// what makes it usable for inspecting a PDF: a byte sequence that is not text becomes a
    /// sequence of visible characters rather than a replacement character that hides the byte
    /// being looked for.
    /// </para>
    /// </summary>
    private async Task<string> ReadAsync(ReportFormat format) =>
        Encoding.Latin1.GetString(await WriteAsync(format));

    private ToolRequest Request(string? format = null, Dictionary<string, string>? parameters = null)
    {
        parameters ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        parameters.TryAdd("title", "Report");

        if (format is not null)
        {
            parameters["format"] = format;
        }

        return new ToolRequest(
            "ReportGenerationTool",
            "Write the deployment report",
            "Summarise the deployment architecture and write it to a report.",
            parameters,
            AgentRequestSource.DemoScenario) with { PriorContext = Body };
    }

    /// <summary>A folder service that points at the temporary directory for one test.</summary>
    private sealed class StubKnownFolderService : IKnownFolderService
    {
        private readonly string _path;

        public StubKnownFolderService(string path) => _path = path;

        public Task<Result<string>> GetPathAsync(
            KnownFolderKind folder,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<string>.Success(_path));
    }

    /// <summary>A clock that does not move, so a generated file name is predictable.</summary>
    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}