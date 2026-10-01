using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Abstractions.Documents;
using WindowsAIAssistant.Core.Abstractions.Files;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Agents;
using WindowsAIAssistant.Core.Models.Documents;

namespace WindowsAIAssistant.Application.Agents.Tools;

/// <summary>
/// Reads one document and summarizes it, or answers a question about it.
/// <para>
/// The file is named by a path the tool resolves rather than opens blind: a request that mentions
/// a document turns into a search, the search's best match is checked to be a file that exists,
/// and only then is that exact path read. A path arriving from a model is never followed as
/// given, which is what keeps a plan from being talked into reading something the person never
/// named.
/// </para>
/// <para>
/// Reading a document is gated on the cloud-document switch by the service, not here — that
/// decision already has an owner and a wording, and a second one in a tool would be a second
/// place for them to disagree. What this tool adds is the refusal when no file could be
/// identified, which is the common failure and the one worth saying clearly.
/// </para>
/// <para>
/// Read-only, so it declares no actions and never interrupts a person.
/// </para>
/// </summary>
public sealed partial class DocumentAnalysisTool : ITool
{
    private readonly IDocumentAnalysisService _documents;
    private readonly IFileSearchService _fileSearch;
    private readonly ILogger<DocumentAnalysisTool> _logger;

    public DocumentAnalysisTool(
        IDocumentAnalysisService documents,
        IFileSearchService fileSearch,
        ILogger<DocumentAnalysisTool> logger)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(fileSearch);
        ArgumentNullException.ThrowIfNull(logger);

        _documents = documents;
        _fileSearch = fileSearch;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "DocumentAnalysisTool";

    /// <inheritdoc />
    public string Description =>
        "Read a single document and summarize it, or answer a question about it. Use when one " +
        "named file is involved, rather than a search across everything indexed.";

    /// <inheritdoc />
    public IReadOnlyList<string> RequiredInputs => ["request"];

    /// <summary>
    /// Declares that this tool changes nothing. It opens a file the person named and reads it.
    /// Reading a file is not one of the actions a person is asked to approve — they asked for it
    /// by naming it, and the switch that governs sending it elsewhere is the one declared above.
    /// </summary>
    public IReadOnlyList<AgentAction> DescribeActions(AgentStep step) => [];

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(
        ToolRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();
        var spoken = request.GetParameter("request") ?? request.Goal;
        var (filePath, question) = SplitRequest(spoken);

        var located = await LocateAsync(request, filePath, cancellationToken);

        if (located.IsFailure)
        {
            return ToolResult.Failure(
                Name,
                located.ErrorCode!,
                located.ErrorMessage!);
        }

        try
        {
            var analysis = question is null
                ? await _documents.SummarizeAsync(
                    DocumentAnalysisRequest.Summarize(located.Value!),
                    cancellationToken)
                : await _documents.AskQuestionAsync(
                    DocumentAnalysisRequest.AskQuestion(located.Value!, question),
                    cancellationToken);

            stopwatch.Stop();

            if (!analysis.IsSuccess)
            {
                return ToolResult.Failure(
                    Name,
                    analysis.ErrorCode ?? ErrorCodes.AgentStepFailed,
                    analysis.ErrorMessage ?? "I could not read that document just now.");
            }

            return ToolResult.Success(
                Name,
                analysis.Text,
                [analysis.FileName],
                BuildData(analysis, located.Value!),
                stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Document analysis failed.");

            return ToolResult.Failure(
                Name,
                ErrorCodes.AgentStepFailed,
                "I could not read that document just now.");
        }
    }

    /// <summary>
    /// Resolves the file to read, either from a path given directly or by searching for the name
    /// in the request.
    /// <para>
    /// A path that is given is used only if it names a file that exists. A path that does not
    /// exist is not created, and a path that points somewhere unexpected is not read: the check
    /// is that the thing is a real file, because anything beyond that belongs to a consent
    /// decision the tool is not in a position to make on its own.
    /// </para>
    /// </summary>
    private async Task<Core.Common.Result<string>> LocateAsync(
        ToolRequest request,
        string? namedFile,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(namedFile))
        {
            var candidate = namedFile.Trim().Trim('"', '\'', ' ');

            if (File.Exists(candidate))
            {
                return Core.Common.Result<string>.Success(Path.GetFullPath(candidate));
            }

            // A bare name like "report.pdf" is far more likely to be a name than a path, so it
            // goes to the search below rather than being reported as a missing file.
            if (Path.IsPathFullyQualified(candidate))
            {
                return Core.Common.Result<string>.Failure(
                    ErrorCodes.AgentPathRefused,
                    $"There is no file at {candidate}, so nothing was read.");
            }
        }

        var searchTerm = FileNameFrom(request) ?? namedFile;

        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return Core.Common.Result<string>.Failure(
                ErrorCodes.AgentStepFailed,
                "I could not tell which document to read. Try naming the file.");
        }

        try
        {
            var results = await _fileSearch.SearchAsync(searchTerm, cancellationToken);
            var best = results
                .OrderByDescending(result => result.RelevanceScore)
                .FirstOrDefault();

            if (best is null)
            {
                return Core.Common.Result<string>.Failure(
                    ErrorCodes.AgentStepFailed,
                    $"I could not find a document called \"{searchTerm}\".");
            }

            return Core.Common.Result<string>.Success(best.FullPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not search for a document to read.");

            return Core.Common.Result<string>.Failure(
                ErrorCodes.AgentStepFailed,
                "I could not look for that document just now.");
        }
    }

    /// <summary>
    /// Separates the file to read from a question about it, in either order, because both
    /// "summarize report.pdf" and "what does report.pdf say about X" are reasonable requests and
    /// a tool that could only read one of them would answer the other with a guess.
    /// </summary>
    private static (string? File, string? Question) SplitRequest(string? request)
    {
        if (string.IsNullOrWhiteSpace(request))
        {
            return (null, null);
        }

        var text = request.Trim();
        var file = FileExtensionPattern().Match(text);

        if (!file.Success)
        {
            return (null, null);
        }

        var fileName = file.Value.Trim();
        var remainder = text.Replace(fileName, " ", StringComparison.OrdinalIgnoreCase).Trim();

        // Anything left over that is not a bare instruction is the question. A leftover of
        // "summarize this document" is a command, not a question, and is dropped.
        var isQuestion = remainder.Length > 0
            && !remainder.Contains("summar", StringComparison.OrdinalIgnoreCase)
            && !remainder.Contains("explain", StringComparison.OrdinalIgnoreCase)
            && !remainder.Contains("analy", StringComparison.OrdinalIgnoreCase)
            && !remainder.Contains("read", StringComparison.OrdinalIgnoreCase)
            && !remainder.Contains("document", StringComparison.OrdinalIgnoreCase)
            && !remainder.Contains("file", StringComparison.OrdinalIgnoreCase);

        return (fileName, isQuestion ? remainder : null);
    }

    /// <summary>Reads an explicit file path from the parameters, when the planner supplied one.</summary>
    private static string? FileNameFrom(ToolRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.GetParameter("file")
            ?? request.GetParameter("path")
            ?? request.GetParameter("fileName");
    }

    /// <summary>Builds the metadata, naming the file and the warning count but holding no text.</summary>
    private static IReadOnlyDictionary<string, string> BuildData(DocumentAnalysisResult analysis, string path)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(path);

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["fileName"] = analysis.FileName,
            ["fileType"] = analysis.FileType.ToString(),
            ["path"] = path,
            ["warningCount"] = analysis.Warnings.Count.ToString(CultureInfo.InvariantCulture),
            ["characters"] = analysis.Text.Length.ToString(CultureInfo.InvariantCulture),
            ["summary"] = $"Read \"{analysis.FileName}\" ({analysis.Text.Length} characters).",
        };
    }

    /// <summary>
    /// Finds a file name in free text: a run of non-space characters ending in a short extension.
    /// <para>
    /// Generated rather than interpreted, because this runs against text that arrived from a
    /// microphone and a document page, and a pattern evaluated character by character on every
    /// one of those is work that does not belong in the request path.
    /// </para>
    /// </summary>
    [GeneratedRegex(@"[^\s""']+\.[A-Za-z0-9]{1,8}", RegexOptions.IgnoreCase)]
    private static partial Regex FileExtensionPattern();
}
