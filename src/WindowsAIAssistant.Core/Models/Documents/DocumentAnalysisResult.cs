using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Documents;

/// <summary>
/// What came of reading a document: the text that was produced, where it came from, and what
/// was left out.
/// <para>
/// A failure is returned in this same shape rather than thrown, for the same reason the AI
/// service does it that way: the voice path needs the safe sentence and the code to speak and
/// to branch on, and an exception on the way out would force it to reconstruct both.
/// </para>
/// </summary>
public sealed record DocumentAnalysisResult
{
    private DocumentAnalysisResult(
        DocumentAnalysisOperation operation,
        string fileName,
        DocumentFileType fileType,
        string text,
        IReadOnlyList<string> references,
        IReadOnlyList<DocumentWarning> warnings,
        DocumentMetadata? metadata,
        string? errorCode,
        string? errorMessage)
    {
        Operation = operation;
        FileName = fileName;
        FileType = fileType;
        Text = text;
        References = references;
        Warnings = warnings;
        Metadata = metadata;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>Gets whether the document was to be summarized or questioned.</summary>
    public DocumentAnalysisOperation Operation { get; }

    /// <summary>Gets the document's name, without its directory.</summary>
    public string FileName { get; }

    /// <summary>Gets which family of document was read.</summary>
    public DocumentFileType FileType { get; }

    /// <summary>Gets the summary or the answer.</summary>
    public string Text { get; }

    /// <summary>
    /// Gets the places in the document the text came from, phrased so they can be shown or
    /// spoken. Empty when nothing was produced. Never invented: an answer with no references
    /// says so rather than guessing a plausible-looking page.
    /// </summary>
    public IReadOnlyList<string> References { get; }

    /// <summary>Gets the limitations met while reading the document.</summary>
    public IReadOnlyList<DocumentWarning> Warnings { get; }

    /// <summary>Gets what is known about the document, when it was read successfully.</summary>
    public DocumentMetadata? Metadata { get; }

    /// <summary>Gets the stable code for a failure, and null on success.</summary>
    public string? ErrorCode { get; }

    /// <summary>Gets the sentence to show or speak on a failure, and null on success.</summary>
    public string? ErrorMessage { get; }

    /// <summary>Gets a value indicating whether the analysis produced text.</summary>
    public bool IsSuccess => ErrorCode is null && !string.IsNullOrWhiteSpace(Text);

    /// <summary>Creates a successful result.</summary>
    public static DocumentAnalysisResult Success(
        DocumentAnalysisOperation operation,
        string fileName,
        DocumentFileType fileType,
        string text,
        IEnumerable<string>? references = null,
        IEnumerable<DocumentWarning>? warnings = null,
        DocumentMetadata? metadata = null) =>
        new(
            operation,
            fileName,
            fileType,
            text,
            references is null ? [] : [.. references],
            warnings is null ? [] : [.. warnings],
            metadata,
            errorCode: null,
            errorMessage: null);

    /// <summary>Creates a failed result carrying a code and a sentence fit to show or speak.</summary>
    public static DocumentAnalysisResult Failure(
        DocumentAnalysisOperation operation,
        string fileName,
        DocumentFileType fileType,
        string errorCode,
        string errorMessage) =>
        new(
            operation,
            fileName,
            fileType,
            string.Empty,
            [],
            [],
            metadata: null,
            errorCode,
            errorMessage);
}
