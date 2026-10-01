using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Reports;

/// <summary>
/// What a report is: a title, a body, and when it was made.
/// <para>
/// That is the whole model, and it is deliberately the smallest thing that can be rendered. A
/// report type with sections, tables, headers, and footers would be a document library, and every
/// field on it would be a place for a step's material to be rearranged into a shape nobody
/// reviewed.
/// </para>
/// <para>
/// The body is Markdown regardless of the format it will be written in. Each writer is responsible
/// for rendering that markup in its own format, which means a plain-text report keeps the
/// heading markers out and a Word one turns them into styles. Carrying the markup rather than a
/// parsed tree keeps the writers independent and keeps a Markdown parser out of the agent layer.
/// </para>
/// </summary>
public sealed record ReportDocument
{
    /// <summary>How much text a report may hold, as a guard against a runaway plan.</summary>
    public const int MaximumBodyCharacters = 2_000_000;

    public ReportDocument(string title, string body, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(body);

        Title = title.Trim();
        Body = body;
        CreatedAt = createdAt;
    }

    /// <summary>Gets the title, shown at the top of the report.</summary>
    public string Title { get; }

    /// <summary>Gets the body, as Markdown.</summary>
    public string Body { get; }

    /// <summary>Gets when the report was made.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Creates a report from a title and a body, stamped with the current time.
    /// </summary>
    public static ReportDocument Create(string title, string body) =>
        new(title, body, DateTimeOffset.UtcNow);

    /// <summary>
    /// Reports whether this report has anything to write, and says why not when it does not.
    /// <para>
    /// A body that is empty or only whitespace is refused here rather than by each writer. A
    /// writer asked to render nothing has two reasonable behaviours — an empty file or a
    /// failure — and this makes the choice once.
    /// </para>
    /// </summary>
    public bool TryValidate([System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? refusal)
    {
        if (string.IsNullOrWhiteSpace(Body))
        {
            refusal = "There was nothing to write, so no file was created.";
            return false;
        }

        if (Body.Length > MaximumBodyCharacters)
        {
            refusal =
                $"That report is too long to write ({Body.Length} characters, the limit is " +
                $"{MaximumBodyCharacters}).";
            return false;
        }

        refusal = null;
        return true;
    }
}

/// <summary>
/// What a writer produced: the bytes, or a failure with a sentence to show a person.
/// <para>
/// Bytes rather than a path, because the tool owns the path. A writer that reported where it had
/// written to would be making a claim about a decision it did not make, and the approval a
/// person gave named a location this object knows nothing about.
/// </para>
/// </summary>
public sealed record ReportWriteResult
{
    private ReportWriteResult(byte[]? content, Result failure)
    {
        Content = content;
        Outcome = failure;
    }

    /// <summary>Gets the rendered bytes, or <see langword="null"/> when the write failed.</summary>
    public byte[]? Content { get; }

    /// <summary>Gets the failure, or a success when there was none.</summary>
    public Result Outcome { get; }

    /// <summary>Gets a value indicating whether the report was rendered.</summary>
    public bool IsSuccess => Content is not null;

    /// <summary>Gets how many bytes were rendered, for a log line.</summary>
    public int Length => Content?.Length ?? 0;

    /// <summary>Creates a successful result.</summary>
    public static ReportWriteResult Success(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new ReportWriteResult(content, Result.Success());
    }

    /// <summary>Creates a failed result with a stable code and the sentence to show.</summary>
    public static ReportWriteResult Failure(string errorCode, string message) =>
        new(null, Result.Failure(errorCode, message));
}