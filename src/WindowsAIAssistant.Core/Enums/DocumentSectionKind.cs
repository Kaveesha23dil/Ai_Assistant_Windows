namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// How a structural division of a document should be described when citing it.
/// <para>
/// Answers say where something came from, so the kind of division decides what that reference
/// reads like. Without this, a spreadsheet answer would cite "Page 1" and be silently wrong.
/// </para>
/// </summary>
public enum DocumentSectionKind
{
    /// <summary>The document has no meaningful internal structure, so the whole file is one block.</summary>
    Block = 0,

    /// <summary>A page of a paginated document such as a PDF.</summary>
    Page = 1,

    /// <summary>A heading and the text that belongs under it.</summary>
    Heading = 2,

    /// <summary>A single slide of a presentation.</summary>
    Slide = 3,

    /// <summary>One worksheet of a workbook.</summary>
    Sheet = 4,

    /// <summary>A table lifted out of a word-processing document.</summary>
    Table = 5,
}
