using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Documents;

/// <summary>
/// A document that has been read: what it is, what is in it, and what was left out.
/// <para>
/// The extracted text is held once, in the sections. The whole-document text is assembled from
/// those sections on demand rather than stored beside them, because for a large document that
/// would mean keeping two copies of the same megabytes alive for as long as the analysis
/// takes, and the sections are the copy that carries the page numbers an answer needs.
/// </para>
/// </summary>
public sealed class DocumentContent
{
    private readonly IReadOnlyList<DocumentSection> _sections;
    private string? _text;

    private DocumentContent(
        string fileName,
        DocumentFileType fileType,
        DocumentMetadata metadata,
        IReadOnlyList<DocumentSection> sections,
        IReadOnlyList<DocumentWarning> warnings)
    {
        FileName = fileName;
        FileType = fileType;
        Metadata = metadata;
        _sections = sections;
        ExtractionWarnings = warnings;
        CharacterCount = sections.Sum(section => section.Text.Length);
    }

    /// <summary>Gets the file's name, without any directory part.</summary>
    public string FileName { get; }

    /// <summary>Gets which family of document this is.</summary>
    public DocumentFileType FileType { get; }

    /// <summary>Gets what is known about the file and the document itself.</summary>
    public DocumentMetadata Metadata { get; }

    /// <summary>Gets the document divided into its natural parts, in reading order.</summary>
    public IReadOnlyList<DocumentSection> Sections => _sections;

    /// <summary>Gets the limitations met while reading, which did not stop the reading.</summary>
    public IReadOnlyList<DocumentWarning> ExtractionWarnings { get; }

    /// <summary>Gets the total number of characters extracted across every section.</summary>
    public int CharacterCount { get; }

    /// <summary>Gets an estimate of how many tokens the extracted text is worth.</summary>
    public int EstimatedTokenCount => TokenEstimator.EstimateTokens(RawText);

    /// <summary>Gets a value indicating whether any text at all was extracted.</summary>
    /// <summary>
    /// Gets a value indicating whether the document had nothing in it worth reading.
    /// <para>
    /// Whitespace counts as nothing. A file of blank lines and tabs has been read perfectly well
    /// and contains no content, and a summary asked for of it would otherwise be a summary of
    /// nothing presented as though it were a summary of a document.
    /// </para>
    /// </summary>
    public bool IsEmpty => _sections.All(section => string.IsNullOrWhiteSpace(section.Text));

    /// <summary>
    /// Gets the document as one piece of text, sections separated by blank lines. Built once
    /// and held, since a summary and a question in the same session will both ask for it.
    /// </summary>
    public string RawText => _text ??= string.Join(
        "\n\n",
        _sections.Select(section => section.Text).Where(text => !string.IsNullOrWhiteSpace(text)));

    /// <summary>Reads a document's parts and limitations into a single value.</summary>
    public static DocumentContent Create(
        string fileName,
        DocumentFileType fileType,
        DocumentMetadata metadata,
        IEnumerable<DocumentSection> sections,
        IEnumerable<DocumentWarning>? warnings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(sections);

        return new DocumentContent(
            fileName,
            fileType,
            metadata,
            [.. sections],
            warnings is null ? [] : [.. warnings]);
    }
}
