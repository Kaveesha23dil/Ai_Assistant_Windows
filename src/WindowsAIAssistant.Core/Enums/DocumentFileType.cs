namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// The document families this build can read.
/// <para>
/// This is a family, not a format. A reader that cannot get at the text inside a PDF reports
/// what kind of document it was, so the reason a file could not be read survives even when the
/// reason is "the pages are photographs" rather than "the extension is wrong".
/// </para>
/// </summary>
public enum DocumentFileType
{
    /// <summary>The format is not one this build reads.</summary>
    Unknown = 0,

    /// <summary>A PDF document.</summary>
    Pdf = 1,

    /// <summary>An Office Open XML word-processing document.</summary>
    Word = 2,

    /// <summary>An Office Open XML presentation.</summary>
    PowerPoint = 3,

    /// <summary>An Office Open XML workbook.</summary>
    Excel = 4,

    /// <summary>
    /// Text of some kind: plain text, Markdown, CSV, JSON, XML, or source code. The content
    /// differs but the reading does not, and keeping them together means a new text extension
    /// is a configuration entry rather than a new parser.
    /// </summary>
    PlainText = 5,
}
