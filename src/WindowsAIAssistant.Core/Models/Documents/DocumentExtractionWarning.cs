using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Documents;

/// <summary>
/// A limitation met while reading a document, described so a person can act on it.
/// <para>
/// The wording is part of the value, not decoration attached to it. A warning is shown in the
/// interface and can be spoken by the voice path, so it has to say what happened and what
/// would be needed, in one sentence, without naming a library or a file path.
/// </para>
/// </summary>
/// <param name="Kind">The limitation, in a form that can be tested against.</param>
/// <param name="Message">A single sentence explaining it to the person who asked.</param>
public sealed record DocumentWarning(DocumentWarningKind Kind, string Message)
{
    /// <summary>
    /// Gets the sentence, trimmed. A blank message is refused rather than stored: a warning
    /// that says nothing is worse than no warning, because it looks like something was
    /// reported.
    /// </summary>
    public string Message { get; init; } = string.IsNullOrWhiteSpace(Message)
        ? throw new ArgumentException("A warning must say what happened.", nameof(Message))
        : Message.Trim();
}

/// <summary>
/// The phrases used when a document could not be read completely.
/// <para>
/// They live in one place so that "the page limit" reads the same however it was reached, and
/// so a test can assert that no path ever produces a different wording for the same problem.
/// </para>
/// </summary>
public static class DocumentWarningMessages
{
    /// <summary>The document held more text than this build is willing to analyse.</summary>
    public const string ContentTruncated =
        "Only part of this document was analyzed because it is longer than the configured limit.";

    /// <summary>A worksheet was cut short at the row, column, or cell limit.</summary>
    public const string SpreadsheetTruncated =
        "Only part of this spreadsheet was analyzed.";

    /// <summary>The pages appear to be images rather than text.</summary>
    public const string ScannedPagesDetected =
        "This document appears to contain scanned pages or images. OCR is not enabled yet.";

    /// <summary>The bytes were not read as any confident encoding.</summary>
    public const string UnknownEncoding =
        "This file's text could not be decoded with confidence, so some characters may be wrong.";

    /// <summary>The document is protected.</summary>
    public const string PasswordRequired =
        "This document is password protected, so its contents were not read.";

    /// <summary>The document holds images.</summary>
    public const string ImagesNotAnalyzed =
        "Images are not analyzed in this release.";

    /// <summary>The document holds embedded objects.</summary>
    public const string EmbeddedContentSkipped =
        "Embedded content is not analyzed in this release.";

    /// <summary>Nothing readable was found.</summary>
    public const string EmptyContent =
        "No readable text was found in this document.";

    /// <summary>Gets the sentence for a warning kind.</summary>
    public static string For(DocumentWarningKind kind) => kind switch
    {
        DocumentWarningKind.ContentTruncated => ContentTruncated,
        DocumentWarningKind.SpreadsheetTruncated => SpreadsheetTruncated,
        DocumentWarningKind.ScannedPagesDetected => ScannedPagesDetected,
        DocumentWarningKind.UnknownEncoding => UnknownEncoding,
        DocumentWarningKind.PasswordRequired => PasswordRequired,
        DocumentWarningKind.ImagesNotAnalyzed => ImagesNotAnalyzed,
        DocumentWarningKind.EmbeddedContentSkipped => EmbeddedContentSkipped,
        DocumentWarningKind.EmptyContent => EmptyContent,
        _ => ErrorCodes.UnknownError,
    };

    /// <summary>Creates a warning of a kind, using that kind's sentence.</summary>
    public static DocumentWarning Create(DocumentWarningKind kind) =>
        new(kind, For(kind));
}
