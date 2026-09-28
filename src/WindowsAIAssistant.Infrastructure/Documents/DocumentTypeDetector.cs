using WindowsAIAssistant.Core.Abstractions.Documents;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Infrastructure.Documents;

/// <summary>
/// Works out what kind of document a file is from its name, and confirms it from its bytes
/// where that is cheap.
/// <para>
/// The extension decides, because it is what the person chose and what the other tools on their
/// machine will use too. The bytes are then checked, but only to catch the case where the name
/// is wrong: a file called <c>report.txt</c> whose first four bytes are <c>%PDF</c> is a PDF
/// that someone renamed, and reading it as text would produce a page of punctuation instead of
/// an honest "this is not what its name says".
/// </para>
/// <para>
/// Content types are deliberately not consulted. They are supplied by the browser or the shell
/// from the extension anyway, so trusting one would only add a way to be wrong.
/// </para>
/// </summary>
public sealed class DocumentTypeDetector : IDocumentTypeDetector
{
    private static readonly Dictionary<string, DocumentFileType> ByExtension =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = DocumentFileType.Pdf,
            [".docx"] = DocumentFileType.Word,
            [".pptx"] = DocumentFileType.PowerPoint,
            [".xlsx"] = DocumentFileType.Excel,
            [".txt"] = DocumentFileType.PlainText,
            [".md"] = DocumentFileType.PlainText,
            [".csv"] = DocumentFileType.PlainText,
            [".json"] = DocumentFileType.PlainText,
            [".xml"] = DocumentFileType.PlainText,
            [".cs"] = DocumentFileType.PlainText,
            [".xaml"] = DocumentFileType.PlainText,
            [".csproj"] = DocumentFileType.PlainText,
            [".sln"] = DocumentFileType.PlainText,
            [".log"] = DocumentFileType.PlainText,
        };

    /// <summary>
    /// The formats that share an extension with a supported one but whose contents this build
    /// cannot read. These are the legacy binary Office files, which are still common enough that
    /// being asked about one will happen, and which produce nothing at all when opened with the
    /// modern reader.
    /// </summary>
    private static readonly HashSet<string> LegacyBinaryOfficeExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".doc", ".xls", ".ppt" };

    private static readonly byte[] PdfSignature = "%PDF"u8.ToArray();
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] OleCompoundSignature =
        [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    /// <inheritdoc />
    public IReadOnlyCollection<string> SupportedExtensions { get; } =
        [.. ByExtension.Keys.Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Gets a value indicating whether an extension names a legacy binary Office file.
    /// <para>
    /// Asked separately from <see cref="IsSupported"/> because being able to say what is wrong
    /// and what to do about it is worth more than a refusal: these files are still common, and
    /// there is a fix, which is saving them in the current format.
    /// </para>
    /// </summary>
    public static bool IsLegacyOfficeExtension(string? extension) =>
        !string.IsNullOrEmpty(extension) &&
        LegacyBinaryOfficeExtensions.Contains(extension);

    /// <summary>
    /// Gets a value indicating whether an extension is one of the plain-text ones.
    /// <para>
    /// Asked of the same table that decides a document's type, so the text reader and the type
    /// detector can never disagree about which files are text. Two lists would be one more thing
    /// to forget when a format is added, and the disagreement would only show up as a text file
    /// the reader refuses.
    /// </para>
    /// </summary>
    public static bool IsPlainTextExtension(string? extension) =>
        !string.IsNullOrEmpty(extension) &&
        ByExtension.TryGetValue(extension, out var fileType) &&
        fileType == DocumentFileType.PlainText;

    /// <inheritdoc />
    public DocumentFileType Detect(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var extension = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(extension))
        {
            return DocumentFileType.Unknown;
        }

        return ByExtension.TryGetValue(extension, out var fileType) ? fileType : DocumentFileType.Unknown;
    }

    /// <inheritdoc />
    public bool IsSupported(string filePath)
    {
        if (Detect(filePath) is DocumentFileType.Unknown)
        {
            return false;
        }

        return !LooksLikeLegacyOfficeFile(filePath);
    }

    /// <summary>
    /// Gets the family a file actually is, from its leading bytes. Used to correct a file whose
    /// name does not match its contents, and to recognize the Office formats, which are all
    /// zip archives and so are told apart by what is inside them.
    /// </summary>
    public DocumentFileType DetectFromSignature(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        Span<byte> header = stackalloc byte[8];
        var read = 0;

        try
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 8,
                FileOptions.SequentialScan);

            while (read < header.Length)
            {
                var count = stream.Read(header[read..]);
                if (count == 0)
                {
                    break;
                }

                read += count;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The file is unreadable here, which is a decision for the reader to make with a
            // proper error, not for a signature check to guess at.
            return DocumentFileType.Unknown;
        }

        if (read >= PdfSignature.Length && header[..PdfSignature.Length].SequenceEqual(PdfSignature))
        {
            return DocumentFileType.Pdf;
        }

        if (read >= OleCompoundSignature.Length &&
            header[..OleCompoundSignature.Length].SequenceEqual(OleCompoundSignature))
        {
            return DocumentFileType.Unknown;
        }

        if (read >= ZipSignature.Length && header[..ZipSignature.Length].SequenceEqual(ZipSignature))
        {
            return DetectOfficeFormatFromPackage(filePath);
        }

        return DocumentFileType.Unknown;
    }

    private static DocumentFileType DetectOfficeFormatFromPackage(string filePath)
    {
        try
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(filePath);

            // Every Office Open XML package says which application wrote it, in a part near the
            // root. Reading that is cheaper and far more reliable than inferring the format
            // from the set of entries.
            if (archive.Entries.Any(entry =>
                    entry.FullName.Equals("word/document.xml", StringComparison.OrdinalIgnoreCase)))
            {
                return DocumentFileType.Word;
            }

            if (archive.Entries.Any(entry =>
                    entry.FullName.Equals("ppt/presentation.xml", StringComparison.OrdinalIgnoreCase)))
            {
                return DocumentFileType.PowerPoint;
            }

            if (archive.Entries.Any(entry =>
                    entry.FullName.Equals("xl/workbook.xml", StringComparison.OrdinalIgnoreCase)))
            {
                return DocumentFileType.Excel;
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return DocumentFileType.Unknown;
        }

        return DocumentFileType.Unknown;
    }

    private static bool LooksLikeLegacyOfficeFile(string filePath)
    {
        if (!LegacyBinaryOfficeExtensions.Contains(Path.GetExtension(filePath)))
        {
            return false;
        }

        try
        {
            Span<byte> header = stackalloc byte[8];
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 8,
                FileOptions.SequentialScan);

            var read = stream.ReadAtLeast(header, 8, throwOnEndOfStream: false);
            return read >= OleCompoundSignature.Length &&
                   header[..OleCompoundSignature.Length].SequenceEqual(OleCompoundSignature);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
