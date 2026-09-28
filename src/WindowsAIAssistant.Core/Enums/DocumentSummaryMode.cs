namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// How much of a document a summary should cover.
/// <para>
/// The modes are a request about depth, not about length in characters. A short summary of a
/// long document can be longer than a detailed summary of a one-page note, so the mode
/// changes what the reader is asked to keep rather than how many words it may use.
/// </para>
/// </summary>
public enum DocumentSummaryMode
{
    /// <summary>A few bullet points covering only the most important points.</summary>
    Short = 0,

    /// <summary>The main subjects and findings, at the length most questions need.</summary>
    Standard = 1,

    /// <summary>
    /// A structured account with sections, key findings, and important details, for someone
    /// who intends to act on the document.
    /// </summary>
    Detailed = 2,
}
