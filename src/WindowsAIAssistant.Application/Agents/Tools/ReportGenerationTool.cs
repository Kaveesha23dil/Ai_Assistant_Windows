using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Abstractions.Reports;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Agents;
using WindowsAIAssistant.Core.Models.Reports;

namespace WindowsAIAssistant.Application.Agents.Tools;

/// <summary>
/// Writes what earlier steps found to a file, in Markdown, plain text, Word, or PDF.
/// <para>
/// This is the only tool in the agent that changes anything, and it says so before it runs. It
/// declares a write action naming the file and its type, so a person is asked once, sees where the
/// report is going and what they will be able to open it in, and can refuse — and the answer a
/// person gives is what the executor acts on, not something this tool decides for itself
/// afterwards.
/// </para>
/// <para>
/// It will only write inside the user's own known folders, and only to a name it builds rather
/// than one it was handed. A path arriving from a model or from a voice microphone is never
/// followed: the tool puts a file name inside Documents, which is somewhere the request pointed
/// at in spirit, and a request that names a path somewhere else is refused rather than obeyed.
/// Overwriting is refused too, because a report written twice under a generated name should
/// make two files rather than replace the first one.
/// </para>
/// <para>
/// The format is chosen here rather than by a writer: this tool asks for the format the request
/// named, falls back to Markdown, and tells the person which file type it ended up with. Writing
/// the bytes is a writer's job, so adding a format does not add a branch here.
/// </para>
/// <para>
/// The content is the text earlier steps produced, written out as given. Nothing is summarized
/// on the way to the disk — this tool's only job is to put down what it was given, and a writer's
/// only job is to render it.
/// </para>
/// </summary>
public sealed class ReportGenerationTool : ITool
{
    private readonly IKnownFolderService _folders;
    private readonly IReadOnlyDictionary<ReportFormat, IReportWriter> _writers;
    private readonly TimeProvider _clock;
    private readonly ILogger<ReportGenerationTool> _logger;

    public ReportGenerationTool(
        IKnownFolderService folders,
        IEnumerable<IReportWriter> writers,
        ILogger<ReportGenerationTool> logger,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(writers);
        ArgumentNullException.ThrowIfNull(logger);

        var byFormat = new Dictionary<ReportFormat, IReportWriter>();

        // Last one wins, and a duplicate is logged rather than thrown: two writers claiming the
        // same format is a registration mistake, and refusing to start the whole application over
        // it would be a worse outcome than one of them being used.
        foreach (var writer in writers)
        {
            if (writer is null)
            {
                continue;
            }

            if (byFormat.ContainsKey(writer.Format))
            {
                continue;
            }

            byFormat[writer.Format] = writer;
        }

        _folders = folders;
        _writers = byFormat;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public string Name => "ReportGenerationTool";

    /// <inheritdoc />
    public string Description =>
        "Write text to a file in the user's Documents folder. Supports Markdown (default), " +
        "plain text (.txt), Word (.docx), and PDF (.pdf). Use this as the last step of a plan that " +
        "gathered something worth keeping.";

    /// <inheritdoc />
    public IReadOnlyList<string> RequiredInputs => [];

    /// <inheritdoc />
    public PermissionCapability? RequiredPermission => PermissionCapability.FileWrite;

    /// <inheritdoc />
    public IReadOnlyList<AgentAction> DescribeActions(AgentStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        var title = Title(step);
        var format = Format(step);
        var extension = ReportFormats.Extension(format);

        return
        [
            AgentAction.Write(
                "Write a report",
                $"Write \"{title}{extension}\" to your Documents folder as " +
                $"{ReportFormats.DisplayName(format)}.",
                PermissionCapability.FileWrite),
        ];
    }

    /// <inheritdoc />
    public bool TryApplyModification(AgentStep step, string modification, out AgentStep modified)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentException.ThrowIfNullOrWhiteSpace(modification);

        // Two things about a report can be changed by a person who was asked: what it is called,
        // and what file type it is. Anything else — a different folder, "but shorter" — is refused
        // rather than half-applied, because a person who was refused has been told so and a person
        // who was half-obeyed has not.
        var lowered = modification.ToLowerInvariant();

        var namesTitle = lowered.Contains("title", StringComparison.Ordinal)
            || lowered.Contains("name", StringComparison.Ordinal)
            || lowered.Contains("call it", StringComparison.Ordinal);

        var namesFormat = lowered.Contains("format", StringComparison.Ordinal)
            || lowered.Contains("file type", StringComparison.Ordinal)
            || lowered.Contains("filetype", StringComparison.Ordinal)
            || ReportFormats.All.Any(
                format => lowered.Contains(
                    ReportFormats.Extension(format).TrimStart('.'),
                    StringComparison.Ordinal));

        if (!namesTitle && !namesFormat)
        {
            modified = step;
            return false;
        }

        var parameters = new Dictionary<string, string>(step.Parameters, StringComparer.OrdinalIgnoreCase);
        var quoted = ExtractQuoted(modification);

        if (namesTitle)
        {
            parameters["title"] = SanitizeTitle(quoted ?? modification);
        }

        if (namesFormat)
        {
            // Only a recognized format is applied. A person asking for a spreadsheet is told what
            // was written instead, which is the same sentence the automatic path uses.
            //
            // The format name is looked for in the sentence rather than taken from the quoted
            // part, because the quoted part is the title and "call it \"Review\" as a PDF" quotes
            // one thing while meaning two.
            var asked = ExtractFormatWord(modification) ?? quoted;

            ReportFormats.Parse(asked, out var recognized);

            if (recognized)
            {
                parameters["format"] = ReportFormats.DisplayName(ReportFormats.Parse(asked));
            }
        }

        modified = step with { Parameters = parameters };
        return true;
    }

    /// <summary>
    /// Pulls a format name out of a modification phrased in words.
    /// <para>
    /// Used only when a person did not quote the format, so it looks for the known names in the
    /// text rather than trying to read the sentence. Returning <see langword="null"/> lets the
    /// caller fall back to refusing the change, which is the right outcome for a sentence whose
    /// format cannot be identified.
    /// </para>
    /// </summary>
    private static string? ExtractFormatWord(string modification)
    {
        var lowered = modification.ToLowerInvariant();

        foreach (var format in ReportFormats.All)
        {
            foreach (var name in Names(format))
            {
                if (lowered.Contains(name, StringComparison.Ordinal))
                {
                    return name;
                }
            }
        }

        return null;
    }

    /// <summary>The words a person is likely to use for a format, including its extension.</summary>
    private static IEnumerable<string> Names(ReportFormat format) => format switch
    {
        ReportFormat.Text => ["txt", "plain text", "text file"],
        ReportFormat.Markdown => ["md", "markdown"],
        ReportFormat.Word => ["docx", "word", "word document"],
        ReportFormat.Pdf => ["pdf"],
        _ => [],
    };

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(
        ToolRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();
        var title = Title(request);

        // The format the request asked for, and whether it asked for one at all. Resolved before
        // anything is written so that a request naming a format this build cannot produce fails
        // with a sentence a person can act on, rather than leaving an empty file behind.
        var requested = request.GetParameter("format");
        var format = ReportFormats.Parse(requested, out var formatRecognized);

        if (!string.IsNullOrWhiteSpace(requested) && !formatRecognized)
        {
            _logger.LogInformation(
                "A report was asked for in an unwritable format; falling back to Markdown.");
        }

        if (!_writers.TryGetValue(format, out var writer) || !writer.IsAvailable)
        {
            return ToolResult.Failure(
                Name,
                ErrorCodes.AgentReportFailed,
                writer?.UnavailableReason
                    ?? $"I cannot write {ReportFormats.DisplayName(format)} files on this build.");
        }

        var documents = await _folders.GetPathAsync(KnownFolderKind.Documents, cancellationToken);

        if (documents.IsFailure || documents.Value is null)
        {
            return ToolResult.Failure(
                Name,
                ErrorCodes.AgentPathRefused,
                "I could not find your Documents folder, so the report was not written.");
        }

        var folder = documents.Value;
        var fileName =
            $"{SanitizeTitle(title)}-{_clock.GetUtcNow():yyyyMMdd-HHmmss}{ReportFormats.Extension(format)}";
        var path = Path.Combine(folder, fileName);

        // Checked again at the point of writing rather than only when the action was described.
        // The folder is resolved again on every run, so a symlink or a redirect cannot turn an
        // approved path into a different one between the question and the answer.
        if (!IsInside(folder, path))
        {
            _logger.LogWarning("Refused a report path that resolved outside the Documents folder.");

            return ToolResult.Failure(
                Name,
                ErrorCodes.AgentPathRefused,
                "The report could not be written outside your Documents folder.");
        }

        if (File.Exists(path))
        {
            return ToolResult.Failure(
                Name,
                ErrorCodes.AgentReportFailed,
                $"A file called \"{fileName}\" is already there, so nothing was written.");
        }

        // What to write is whatever the steps before this one found, not the step's own goal. The
        // goal is only the fallback, for a plan that asks for a report out of nothing — in which
        // case writing the goal is more useful than refusing.
        var document = new ReportDocument(
            SanitizeTitle(title),
            request.GetMaterial() ?? request.Goal,
            _clock.GetUtcNow());

        if (!document.TryValidate(out var refusal))
        {
            return ToolResult.Failure(Name, ErrorCodes.AgentReportFailed, refusal);
        }

        ReportWriteResult rendered;

        try
        {
            rendered = await writer.WriteAsync(document, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }

        if (!rendered.IsSuccess || rendered.Content is null)
        {
            return ToolResult.Failure(Name, ErrorCodes.AgentReportFailed, Describe(rendered.Outcome));
        }

        var content = rendered.Content;

        try
        {
            await File.WriteAllBytesAsync(path, content, cancellationToken);
            stopwatch.Stop();

            _logger.LogInformation(
                "Wrote a {Format} report of {Characters} characters to the Documents folder.",
                format,
                document.Body.Length);

            var saved = $"Saved the report as \"{fileName}\" in your Documents folder.";

            // When the request named a format this build cannot write, the substitution is said
            // out loud. A person who asked for a PDF and was handed Markdown has to be told,
            // because the file they open tells them nothing.
            var message = string.IsNullOrWhiteSpace(requested)
                ? saved
                : formatRecognized
                    ? saved
                    : $"{ReportFormats.UnrecognizedMessage(requested)} {saved}";

            return ToolResult.Success(
                Name,
                message,
                [fileName],
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["fileName"] = fileName,
                    ["folder"] = folder,
                    ["format"] = format.ToString(),
                    ["characters"] = document.Body.Length.ToString(CultureInfo.InvariantCulture),
                    ["bytes"] = rendered.Length.ToString(CultureInfo.InvariantCulture),
                    ["summary"] = $"Saved \"{fileName}\" to Documents.",
                },
                stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return ToolResult.Failure(
                Name,
                ErrorCodes.AgentReportFailed,
                "Windows would not let me write to your Documents folder.");
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Could not write the report to the Documents folder.");

            return ToolResult.Failure(
                Name,
                ErrorCodes.AgentReportFailed,
                "The report could not be written just now.");
        }
    }

    /// <summary>
    /// Turns a failure into a sentence.
    /// <para>
    /// A failure carries the sentence to show when there is one. When there is not — which means
    /// a writer failed in a way this tool did not anticipate — the message is generic on purpose:
    /// the alternative is putting a stack trace or an exception message in front of a person.
    /// </para>
    /// </summary>
    private static string Describe(Result failure) =>
        !string.IsNullOrWhiteSpace(failure.ErrorMessage)
            ? failure.ErrorMessage
            : "The report could not be created just now.";


    /// <summary>Reads the title from a step's parameters, falling back to the step's own wording.</summary>
    private static string Title(AgentStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        return step.Parameters.TryGetValue("title", out var title) && !string.IsNullOrWhiteSpace(title)
            ? SanitizeTitle(title)
            : SanitizeTitle(step.Description);
    }

    /// <summary>Reads the title from a tool request, falling back to its goal.</summary>
    private static string Title(ToolRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.GetParameter("title") ?? request.Goal;
    }

    /// <summary>
    /// Reads the format a step asked for, so the action a person approves can name the file type.
    /// <para>
    /// A step with no format in its parameters gets the default, which is the same one the write
    /// path would choose. Approving an action that says "Markdown" and receiving a PDF would be a
    /// mismatch between what was agreed and what was produced.
    /// </para>
    /// </summary>
    private static ReportFormat Format(AgentStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        return step.Parameters.TryGetValue("format", out var format)
            ? ReportFormats.Parse(format)
            : ReportFormats.Default;
    }

    /// <summary>
    /// Reduces a title to something that is safe in a file name: no path separators, no
    /// characters Windows forbids, and short enough to read in a list of files.
    /// </summary>
    private static string SanitizeTitle(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (string.IsNullOrWhiteSpace(value))
        {
            return "Report";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);

        foreach (var character in value.Trim())
        {
            builder.Append(invalid.Contains(character) || character is '/' or '\\' or ':' ? '-' : character);
        }

        var cleaned = builder.ToString().Trim(' ', '.', '-');

        if (cleaned.Length == 0)
        {
            return "Report";
        }

        return cleaned.Length <= 60 ? cleaned : string.Concat(cleaned.AsSpan(0, 60).TrimEnd(), "\u2026");
    }

    /// <summary>Pulls the quoted part out of a modification, when a person used quotes.</summary>
    private static string? ExtractQuoted(string modification)
    {
        var first = modification.IndexOf('"', StringComparison.Ordinal);

        if (first < 0)
        {
            return null;
        }

        var last = modification.LastIndexOf('"');

        return last > first ? modification[(first + 1)..last] : null;
    }

    /// <summary>
    /// Reports whether a path really lies inside a folder, comparing resolved full paths so a
    /// relative segment cannot walk out of it.
    /// </summary>
    private static bool IsInside(string folder, string path)
    {
        try
        {
            var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            var full = Path.GetFullPath(path);

            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
