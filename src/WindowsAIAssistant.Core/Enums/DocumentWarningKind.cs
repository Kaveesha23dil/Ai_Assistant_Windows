namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// Something that came up while reading a document which the reader should know about but
/// which did not stop the reading.
/// <para>
/// These are kept separate from failures on purpose. A scanned page in a forty-page PDF is
/// still a readable PDF, and a spreadsheet cut short at its limits is still worth analysing;
/// making either of them an error would throw away usable work because of an honest
/// limitation.
/// </para>
/// </summary>
public enum DocumentWarningKind
{
    /// <summary>The document yielded no usable text, so it may be images rather than text.</summary>
    ScannedPagesDetected = 0,

    /// <summary>The document held more text than the configured limit allows.</summary>
    ContentTruncated = 1,

    /// <summary>A worksheet was larger than the configured row, column, or cell limits.</summary>
    SpreadsheetTruncated = 2,

    /// <summary>
    /// The file's bytes were not confidently readable as any common encoding, so the text was
    /// decoded on a best-effort basis and may not be exact.
    /// </summary>
    UnknownEncoding = 3,

    /// <summary>The document is protected and its contents were not read.</summary>
    PasswordRequired = 4,

    /// <summary>The document contains images, which are not interpreted.</summary>
    ImagesNotAnalyzed = 5,

    /// <summary>The document contains embedded files or objects, which are not opened.</summary>
    EmbeddedContentSkipped = 6,

    /// <summary>The document contained no text once structure was ignored.</summary>
    EmptyContent = 7,
}
