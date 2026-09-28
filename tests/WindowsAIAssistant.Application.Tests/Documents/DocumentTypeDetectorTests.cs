using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Infrastructure.Documents;

namespace WindowsAIAssistant.Application.Tests.Documents;

/// <summary>
/// Covers deciding what kind of document a file is.
/// <para>
/// The interesting cases are the ones where a person would be misled: a legacy binary Office
/// file wearing a modern extension, and a modern file renamed to look like a text file. Both are
/// detected from the file's own bytes, so the tests here write real bytes rather than relying on
/// a name.
/// </para>
/// </summary>
public sealed class DocumentTypeDetectorTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "waip-type-tests-" + Guid.NewGuid().ToString("N"));

    private readonly DocumentTypeDetector _detector = new();

    public DocumentTypeDetectorTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("report.pdf", DocumentFileType.Pdf)]
    [InlineData("report.PDF", DocumentFileType.Pdf)]
    [InlineData("brief.docx", DocumentFileType.Word)]
    [InlineData("deck.pptx", DocumentFileType.PowerPoint)]
    [InlineData("budget.xlsx", DocumentFileType.Excel)]
    [InlineData("notes.txt", DocumentFileType.PlainText)]
    [InlineData("notes.md", DocumentFileType.PlainText)]
    [InlineData("data.csv", DocumentFileType.PlainText)]
    [InlineData("app.cs", DocumentFileType.PlainText)]
    [InlineData("unknown.xyz", DocumentFileType.Unknown)]
    [InlineData("noextension", DocumentFileType.Unknown)]
    public void TheExtensionDecidesTheType(string fileName, DocumentFileType expected) =>
        Assert.Equal(expected, _detector.Detect(fileName));

    [Fact]
    public void ALegacyOfficeFileIsNotReportedAsSupported()
    {
        // A .doc is not a .docx. It shares the family, not the format, and opening it with the
        // modern reader produces nothing at all.
        var path = Path.Combine(_directory, "old.doc");
        File.WriteAllBytes(path, [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0x00, 0x00]);

        Assert.False(_detector.IsSupported(path));
    }

    [Fact]
    public void ALegacyExtensionIsRefusedWhateverIsInsideIt()
    {
        // The name is enough to refuse it. A legacy .doc is not a .docx: it shares the family,
        // not the format, and opening it with the modern reader produces nothing at all. The
        // bytes are not consulted because they cannot make it readable, and a file with a
        // modern package inside a .doc name is still not something this build should open.
        var path = Path.Combine(_directory, "actually-docx.doc");
        File.WriteAllBytes(path, [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x00, 0x00]);

        Assert.Equal(DocumentFileType.Unknown, _detector.Detect(path));
        Assert.False(_detector.IsSupported(path));
    }

    [Fact]
    public void ATextFileBeginningWithThePdfMarkerIsStillRecognizedAsAPdfByTheSignatureAlone()
    {
        // Worth stating plainly because it looks like a bug and is not: the signature helper
        // sees four bytes and reports a PDF. Text is defined by its extension, because a text
        // file can begin with any characters at all, so sniffing a text format proves nothing.
        // The reader below the detector is what keeps a text file readable, and that it does so
        // is asserted in DocumentReaderTests.
        var path = Path.Combine(_directory, "actually-pdf.txt");
        File.WriteAllText(path, "%PDF-1.7 this is plain text after all");

        Assert.Equal(DocumentFileType.PlainText, _detector.Detect(path));
        Assert.Equal(DocumentFileType.Pdf, _detector.DetectFromSignature(path));
    }

    [Fact]
    public void APdfIsRecognizedFromItsFirstBytes()
    {
        var path = Path.Combine(_directory, "real.pdf");
        File.WriteAllText(path, "%PDF-1.7 and a body that is not read by this test");

        Assert.Equal(DocumentFileType.Pdf, _detector.DetectFromSignature(path));
    }

    [Fact]
    public void TheSupportedExtensionListCoversEveryDeclaredType()
    {
        var extensions = _detector.SupportedExtensions;

        Assert.Contains(".pdf", extensions);
        Assert.Contains(".docx", extensions);
        Assert.Contains(".pptx", extensions);
        Assert.Contains(".xlsx", extensions);

        // Every extension offered has to resolve to a type, or a person would be shown a file
        // as readable and then refused.
        foreach (var extension in extensions)
        {
            Assert.NotEqual(DocumentFileType.Unknown, _detector.Detect($"sample{extension}"));
        }
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".md")]
    [InlineData(".json")]
    [InlineData(".xyz")]
    [InlineData(null)]
    public void OnlyRealTextExtensionsAreAcceptedByTheTextReader(string? extension)
    {
        // The text reader asks this question, and the answer has to come from the same table the
        // detector uses, or one of them will disagree about what a text file is.
        Assert.Equal(extension is not null && _detector.Detect($"x{extension}") == DocumentFileType.PlainText,
            DocumentTypeDetector.IsPlainTextExtension(extension));
    }
}
