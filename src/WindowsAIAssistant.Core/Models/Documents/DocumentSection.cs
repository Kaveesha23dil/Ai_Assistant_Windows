using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Documents;

/// <summary>
/// One structural division of a document, holding the text that belongs to it.
/// <para>
/// Sections are the reason an answer can say "on page 4" without inventing it. The division
/// chosen is the one the format already has — a page, a slide, a worksheet, a heading — so a
/// citation points at something a person can turn to and find, rather than at an offset
/// invented by the chunker.
/// </para>
/// </summary>
public sealed record DocumentSection
{
    /// <summary>Gets the division's place in the document, counting from zero.</summary>
    public int Order { get; init; }

    /// <summary>Gets what kind of division this is, which decides how it is cited.</summary>
    public DocumentSectionKind Kind { get; init; }

    /// <summary>Gets the division's name as it appears in the document.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the short label used when this section is cited, such as "page 4" or "Sheet
    /// &quot;Budget&quot;". It reads as a phrase so it can be dropped straight into a sentence.
    /// </summary>
    public string Reference { get; init; } = string.Empty;

    /// <summary>Gets the text of this division, already normalized for reading.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether this division holds no text at all.</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);

    /// <summary>Creates a section, validating the order and the two names it is given.</summary>
    public static DocumentSection Create(
        int order,
        DocumentSectionKind kind,
        string name,
        string reference,
        string text)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(order);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(text);

        return new DocumentSection
        {
            Order = order,
            Kind = kind,
            Name = name.Trim(),
            Reference = string.IsNullOrWhiteSpace(reference) ? name.Trim() : reference.Trim(),
            Text = text,
        };
    }
}
