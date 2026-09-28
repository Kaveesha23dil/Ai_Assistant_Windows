using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Documents;

namespace WindowsAIAssistant.Application.DTOs;

/// <summary>
/// What the interface needs to show about a document, once it has been read and asked about.
/// <para>
/// Carries the answer and where it came from, and nothing that would be wasteful to hold: no
/// extracted text, no chunk list, no file path. The text was needed to answer the question and
/// has no further use, and a view model is the last place it would sit being kept alive.
/// </para>
/// </summary>
public sealed record DocumentAnalysisDto
{
    public required bool IsSuccess { get; init; }

    public required DocumentAnalysisOperation Operation { get; init; }

    /// <summary>
    /// Gets the document's name, without the folders leading to it. The path is not carried
    /// here: it is not shown, and a value that is never displayed is a value that can still be
    /// logged by accident later.
    /// </summary>
    public required string FileName { get; init; }

    public DocumentFileType FileType { get; init; }

    public string Text { get; init; } = string.Empty;

    public IReadOnlyCollection<string> References { get; init; } = [];

    public IReadOnlyCollection<DocumentWarningDto> Warnings { get; init; } = [];

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public bool IsAnswered => IsSuccess && !string.IsNullOrWhiteSpace(Text);

    /// <summary>
    /// Gets a value indicating whether the document said the answer was not in it, which is a
    /// different outcome from a failure and is worth showing differently.
    /// </summary>
    public bool IsNotInDocument =>
        IsSuccess &&
        Text.Contains(Documents.DocumentPromptBuilder.SourceNotFoundMarker, StringComparison.Ordinal);
}

/// <summary>
/// A limitation met while reading a document, in words fit to put in front of a person.
/// </summary>
public sealed record DocumentWarningDto(
    DocumentWarningKind Kind,
    string Message)
{
    public static DocumentWarningDto From(DocumentWarning warning) =>
        new(warning.Kind, warning.Message);
}
