using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Reports;

namespace WindowsAIAssistant.Core.Abstractions.Reports;

/// <summary>
/// Writes a report in one file format.
/// <para>
/// An interface with a format in front of it, so the report tool can be told which writer to
/// use rather than branching on the format itself. That split is what keeps a Word package, a
/// PDF byte table, and a Markdown heading out of a class whose job is to decide whether it is
/// allowed to write anything at all.
/// </para>
/// <para>
/// Writers return bytes rather than taking a path. The tool owns the path, checks it is inside
/// the folder it is allowed to write to, checks nothing is already there, and then asks for the
/// bytes and writes them itself. A writer given a path would have three copies of the path rules
/// and three chances for them to disagree.
/// </para>
/// </summary>
public interface IReportWriter
{
    /// <summary>Gets the format this writer produces.</summary>
    ReportFormat Format { get; }

    /// <summary>
    /// Gets whether this writer can work on this machine right now.
    /// <para>
    /// False when a dependency the format needs is missing. The report tool reports this as
    /// "that format is not available on this build" rather than failing halfway through writing,
    /// because a half-written document is worse than none.
    /// </para>
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>Gets the sentence explaining why this writer is unavailable.</summary>
    string? UnavailableReason { get; }

    /// <summary>
    /// Renders the report. Returns the bytes to write, or a failure with a sentence to show.
    /// <para>
    /// The report is passed in whole. A writer does not fetch, does not summarize, and does not
    /// decide what belongs in it — it is a format, not an editor.
    /// </para>
    /// </summary>
    Task<ReportWriteResult> WriteAsync(ReportDocument report, CancellationToken cancellationToken = default);
}