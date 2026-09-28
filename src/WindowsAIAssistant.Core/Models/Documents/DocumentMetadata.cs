namespace WindowsAIAssistant.Core.Models.Documents;

/// <summary>
/// What is known about a document before or after reading it.
/// <para>
/// Every field that a format cannot supply is left null rather than guessed. An empty author
/// is a true statement about a spreadsheet; an invented one is a lie the rest of the summary
/// will be built on, and nothing here is ever sent anywhere, so a wrong value would be
/// carried around in every answer that cited it.
/// </para>
/// </summary>
public sealed record DocumentMetadata
{
    /// <summary>Gets the file's name, without any directory part.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Gets the lower-case extension, including the leading dot.</summary>
    public string Extension { get; init; } = string.Empty;

    /// <summary>Gets the size of the file on disk, in bytes.</summary>
    public long FileSize { get; init; }

    /// <summary>Gets when the file was created, when the system reports it.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Gets when the file was last written, when the system reports it.</summary>
    public DateTimeOffset? ModifiedAt { get; init; }

    /// <summary>Gets the document's own title, when the format records one.</summary>
    public string? Title { get; init; }

    /// <summary>Gets the document's author, when the format records one.</summary>
    public string? Author { get; init; }

    /// <summary>Gets the number of pages, for a paginated document.</summary>
    public int? PageCount { get; init; }

    /// <summary>Gets the number of slides, for a presentation.</summary>
    public int? SlideCount { get; init; }

    /// <summary>Gets the number of worksheets, for a workbook.</summary>
    public int? SheetCount { get; init; }
}
