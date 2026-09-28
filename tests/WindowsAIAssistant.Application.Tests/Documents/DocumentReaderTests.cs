using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Application.Tests.Fakes;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Infrastructure.Configuration.Options;
using WindowsAIAssistant.Infrastructure.Documents;
using WindowsAIAssistant.Infrastructure.Documents.Text;
using WindowsAIAssistant.Infrastructure.Documents.Word;

namespace WindowsAIAssistant.Application.Tests.Documents;

/// <summary>
/// Covers the checks a file has to pass before anything reads it, and the text reader that
/// follows them.
/// <para>
/// The failures are the point of these tests. A missing file, a format that is not supported, a
/// file too large to read, and a file with nothing readable in it are four different problems,
/// and a person is told a different thing in each case.
/// </para>
/// </summary>
public sealed class DocumentReaderTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "waip-reader-tests-" + Guid.NewGuid().ToString("N"));

    private readonly IOptionsMonitor<DocumentOptions> _options =
        new StaticOptions(new DocumentOptions());

    public DocumentReaderTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task ATextFileIsReadAndItsWordsComeBack()
    {
        var path = Write("notes.txt", "The renewal date is the third of March.\nPayment is annual.");

        var content = await Reader().ReadAsync(path);

        Assert.Equal(DocumentFileType.PlainText, content.FileType);
        Assert.Equal("notes.txt", content.FileName);
        Assert.Contains("renewal date", content.RawText, StringComparison.OrdinalIgnoreCase);
        Assert.False(content.IsEmpty);
    }

    [Fact]
    public async Task AFileThatDoesNotExistIsNamedRatherThanSwallowed()
    {
        var path = Path.Combine(_directory, "missing.txt");

        var error = await ThrowsAsync(() => Reader().ReadAsync(path));

        Assert.Equal(ErrorCodes.DocumentNotFound, error.ErrorCode);
        Assert.Contains("missing.txt", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownExtensionIsRefusedWithTheFormatItFound()
    {
        var path = Write("model.xyz", "some bytes");

        var error = await ThrowsAsync(() => Reader().ReadAsync(path));

        Assert.Equal(ErrorCodes.DocumentFormatUnsupported, error.ErrorCode);
    }

    [Fact]
    public async Task ALegacyOfficeFileIsRefusedWithAnExplanationRatherThanAnError()
    {
        var path = Path.Combine(_directory, "old.doc");
        File.WriteAllBytes(path, [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0x00]);

        var error = await ThrowsAsync(() => Reader().ReadAsync(path));

        Assert.Equal(ErrorCodes.DocumentFormatUnsupported, error.ErrorCode);

        // The sentence has to say what to do about it, since there is something to do: save the
        // file in the current format.
        Assert.Contains("current format", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AFileOverTheSizeLimitIsRefusedWithoutBeingRead()
    {
        var options = new DocumentOptions { MaximumFileSizeMb = 1 };
        var big = Write("big.txt", new string('x', 1_200_000));

        var error = await ThrowsAsync(() => Reader(options).ReadAsync(big));

        Assert.Equal(ErrorCodes.DocumentTooLarge, error.ErrorCode);
        Assert.Contains("1 megabyte", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AFileWithNothingInItIsReportedAsEmptyRatherThanAsAFailure()
    {
        // A spreadsheet of blank cells is readable and contains nothing. Saying "nothing was
        // found" is a different and more useful answer than reporting that reading it failed.
        var path = Write("blank.txt", "   \r\n\t\r\n");

        var error = await ThrowsAsync(() => Reader().ReadAsync(path));

        Assert.Equal(ErrorCodes.DocumentEmpty, error.ErrorCode);
    }

    [Fact]
    public async Task ReadingTurnedOffInSettingsIsRefusedRatherThanIgnored()
    {
        var path = Write("notes.txt", "content");
        var options = new DocumentOptions { Enabled = false };

        var error = await ThrowsAsync(() => Reader(options).ReadAsync(path));

        Assert.Equal(ErrorCodes.DocumentFormatUnsupported, error.ErrorCode);
    }

    [Fact]
    public async Task ATextFileThatBeginsWithThePdfMarkerIsStillReadAsText()
    {
        // A text file can start with any characters, so the four bytes at the front are not
        // evidence of anything when the extension says text. Refusing this file would be a
        // signature check overruling a decision the extension already settled.
        var path = Write("notes.txt", "%PDF-1.7 is a string some people paste into a text file");

        var content = await Reader().ReadAsync(path);

        Assert.Equal(DocumentFileType.PlainText, content.FileType);
        Assert.Contains("%PDF", content.RawText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePathIsNotKeptOnTheResult()
    {
        // A path is the one part of a document that says where somebody keeps their files. It is
        // needed to open the file and has no use afterwards, so nothing above the reader holds
        // on to it.
        var path = Write("private/notes.txt", "content");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var content = await Reader().ReadAsync(path);

        Assert.Equal("notes.txt", content.FileName);
        Assert.DoesNotContain(_directory, content.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_directory, content.RawText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancellationStopsTheReadRatherThanReturningAPartialDocument()
    {
        var path = Write("notes.txt", new string('x', 100_000));

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Reader().ReadAsync(path, cancellation.Token));
    }

    [Fact]
    public async Task TextBeyondTheCharacterLimitIsDroppedAndTheTruncationIsReported()
    {
        var options = new DocumentOptions { MaximumExtractedCharacters = 100 };
        var path = Write("long.txt", new string('a', 90) + "\n" + new string('b', 500));

        var content = await Reader(options).ReadAsync(path);

        // The point of the limit is the report as much as the cut: a summary that silently
        // stopped halfway would read as a summary of the whole file.
        Assert.Contains(content.ExtractionWarnings, w => w.Kind == DocumentWarningKind.ContentTruncated);
        Assert.True(content.CharacterCount <= 200, $"read {content.CharacterCount} characters");
    }

    [Fact]
    public async Task APlainTextFileIsOneBlockAndAMarkdownFileIsDividedByItsHeadings()
    {
        // Only Markdown carries structure this build relies on: its headings are its sections.
        // A text file has no equivalent, so it is handed over whole and the chunker is what
        // divides it. Asserting the difference keeps the two from being confused later.
        var text = Write("notes.txt", "First heading\nBody of the first.\n\nSecond heading\nBody of the second.");
        var plain = await Reader().ReadAsync(text);
        Assert.Single(plain.Sections);

        var markdown = Write("notes.md", "# First heading\nBody of the first.\n\n## Second heading\nBody of the second.");
        var structured = await Reader().ReadAsync(markdown);

        Assert.True(structured.Sections.Count >= 2, $"read {structured.Sections.Count} sections");
        Assert.Contains(structured.Sections, section => section.Kind == Core.Enums.DocumentSectionKind.Heading);
    }

    [Fact]
    public void TheTextReaderOnlyClaimsTheTextFormats()
    {
        // The reader has to agree with the detector about what a text file is, or one of the two
        // will refuse a file the other is willing to open.
        var extractor = new PlainTextDocumentExtractor(_options, NullLogger<PlainTextDocumentExtractor>.Instance);

        Assert.True(extractor.CanExtract(".txt"));
        Assert.True(extractor.CanExtract(".md"));
        Assert.True(extractor.CanExtract(".csv"));
        Assert.False(extractor.CanExtract(".pdf"));
        Assert.False(extractor.CanExtract(".docx"));
    }

    [Fact]
    public void TheWordReaderOnlyClaimsWordDocuments()
    {
        var extractor = new WordDocumentExtractor(_options, NullLogger<WordDocumentExtractor>.Instance);

        Assert.True(extractor.CanExtract(".docx"));
        Assert.False(extractor.CanExtract(".doc"));
        Assert.False(extractor.CanExtract(".txt"));
    }

    [Fact]
    public async Task TheFactoryHandsOutTheReaderForTheFilesType()
    {
        var factory = Factory();

        Assert.IsType<PlainTextDocumentExtractor>(factory.GetExtractorFor("notes.md"));
        Assert.IsType<WordDocumentExtractor>(factory.GetExtractorFor("brief.docx"));
    }

    [Fact]
    public void TheFactoryRefusesAFormatWithNoReaderRatherThanReturningNothing()
    {
        var error = Assert.Throws<DocumentException>(
            () => Factory().GetExtractor(DocumentFileType.Unknown));

        Assert.Equal(ErrorCodes.DocumentFormatUnsupported, error.ErrorCode);
    }

    private DocumentReader Reader(DocumentOptions? options = null)
    {
        var monitor = options is null
            ? _options
            : new StaticOptions(options);

        return new DocumentReader(
            new DocumentTypeDetector(),
            Factory(options ?? (options is null ? new DocumentOptions() : options)),
            monitor,
            NullLogger<DocumentReader>.Instance);
    }

    private DocumentExtractorFactory Factory(DocumentOptions? options = null)
    {
        var monitor = new StaticOptions(options ?? new DocumentOptions());

        return new DocumentExtractorFactory(
            [
                new PlainTextDocumentExtractor(monitor, NullLogger<PlainTextDocumentExtractor>.Instance),
                new WordDocumentExtractor(monitor, NullLogger<WordDocumentExtractor>.Instance),
            ],
            new DocumentTypeDetector());
    }

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static async Task<DocumentException> ThrowsAsync(Func<Task> work) =>
        await Assert.ThrowsAsync<DocumentException>(work);

    /// <summary>
    /// A monitor that always reports one value. The production monitor exists so a settings save
    /// can be picked up without a restart, which is not what these tests are about.
    /// </summary>
    private sealed class StaticOptions(DocumentOptions value) : IOptionsMonitor<DocumentOptions>
    {
        public DocumentOptions CurrentValue => value;

        public DocumentOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<DocumentOptions, string?> listener) => null;
    }
}
