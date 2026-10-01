namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// The file formats a generated report can be written in.
/// <para>
/// Four, and the choice is the report's to make rather than the planner's. A plan may ask for a
/// format by name and anything it does not name falls back to Markdown, so an unspecified
/// request still produces a file rather than failing — but the set is closed, because every entry
/// here has a writer behind it that this application controls. Accepting an arbitrary extension
/// would mean a planner naming a format that gets written by something else on the machine.
/// </para>
/// <para>
/// The formats are ordered from least to most capable rather than alphabetically, which is the
/// order the workspace lists them in and the order a fallback walks.
/// </para>
/// </summary>
public enum ReportFormat
{
    /// <summary>
    /// Plain text. The safe default for anything that will be pasted somewhere else, and the
    /// only format with no structure to lose.
    /// </summary>
    Text = 0,

    /// <summary>
    /// Markdown. The default for a generated report, because it renders as headings and lists
    /// in the places reports are actually read — a browser, a repository, a chat window — and it
    /// is plain text underneath.
    /// </summary>
    Markdown = 1,

    /// <summary>
    /// A Word document. For a report somebody has to open in Word and edit.
    /// </summary>
    Word = 2,

    /// <summary>
    /// A PDF. For a report that has to look finished and cannot be edited afterwards, which is
    /// also why writing one needs a font embedded rather than a system one assumed present.
    /// </summary>
    Pdf = 3,
}