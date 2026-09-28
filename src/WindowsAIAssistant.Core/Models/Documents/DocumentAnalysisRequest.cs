using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Documents;

/// <summary>What a person is asking a document to do.</summary>
public enum DocumentAnalysisOperation
{
    /// <summary>Describe the document.</summary>
    Summarize = 0,

    /// <summary>Answer a question about the document.</summary>
    AskQuestion = 1,
}

/// <summary>
/// One request to read a document and answer something about it.
/// <para>
/// The question is optional because the two operations need different things: a summary has
/// nothing to ask and a question cannot be asked without one. It is validated at construction
/// so a caller cannot build a request that the reader has to guess the intent of.
/// </para>
/// </summary>
public sealed record DocumentAnalysisRequest
{
    private DocumentAnalysisRequest(
        string filePath,
        DocumentAnalysisOperation operation,
        DocumentSummaryMode summaryMode,
        string? question)
    {
        FilePath = filePath;
        Operation = operation;
        SummaryMode = summaryMode;
        Question = question;
    }

    /// <summary>Gets the full path of the document to read.</summary>
    public string FilePath { get; }

    /// <summary>Gets whether the document is to be summarized or questioned.</summary>
    public DocumentAnalysisOperation Operation { get; }

    /// <summary>Gets how much of the document a summary should cover.</summary>
    public DocumentSummaryMode SummaryMode { get; }

    /// <summary>Gets the question, for <see cref="DocumentAnalysisOperation.AskQuestion"/>.</summary>
    public string? Question { get; }

    /// <summary>
    /// Gets the file's name without its directory. Taken from the path here so that nothing
    /// downstream has to touch the full path again, which is the thing most likely to end up
    /// in a log.
    /// </summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>Asks for a summary of a document.</summary>
    public static DocumentAnalysisRequest Summarize(
        string filePath,
        DocumentSummaryMode mode = DocumentSummaryMode.Standard) =>
        new(filePath, DocumentAnalysisOperation.Summarize, mode, null);

    /// <summary>Asks a question about a document.</summary>
    public static DocumentAnalysisRequest AskQuestion(string filePath, string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        return new DocumentAnalysisRequest(
            filePath,
            DocumentAnalysisOperation.AskQuestion,
            DocumentSummaryMode.Standard,
            question.Trim());
    }
}
