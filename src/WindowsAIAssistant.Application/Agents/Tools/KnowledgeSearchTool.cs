using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Abstractions.Knowledge;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Agents;
using WindowsAIAssistant.Core.Models.Knowledge;

namespace WindowsAIAssistant.Application.Agents.Tools;

/// <summary>
/// Searches the indexed knowledge base and returns the passages found.
/// <para>
/// This is a read-only tool, and it says so by returning nothing from
/// <c>DescribeActions</c>: an empty action list is what tells the executor not to interrupt a
/// person with a dialog. A search changes nothing, so requiring a yes before every one of them
/// would train people to approve without reading, which is the opposite of what the approval
/// gate is for.
/// </para>
/// <para>
/// The answer is returned as content and is never written to the activity store. What is
/// written is the file names and the count, which is what somebody looking at a timeline later
/// wants to know — which documents the answer came from — without the passages themselves being
/// anywhere near the database.
/// </para>
/// </summary>
public sealed class KnowledgeSearchTool : ITool
{
    private readonly IRagRetriever _retriever;
    private readonly IKnowledgeBaseRepository _knowledgeBases;
    private readonly ILogger<KnowledgeSearchTool> _logger;

    public KnowledgeSearchTool(
        IRagRetriever retriever,
        IKnowledgeBaseRepository knowledgeBases,
        ILogger<KnowledgeSearchTool> logger)
    {
        ArgumentNullException.ThrowIfNull(retriever);
        ArgumentNullException.ThrowIfNull(knowledgeBases);
        ArgumentNullException.ThrowIfNull(logger);

        _retriever = retriever;
        _knowledgeBases = knowledgeBases;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "KnowledgeSearchTool";

    /// <inheritdoc />
    public string Description =>
        "Search the documents the user has indexed and return the matching passages. " +
        "Use this when the answer is expected to be grounded in the user's own files.";

    /// <inheritdoc />
    public IReadOnlyList<string> RequiredInputs => ["query"];

    /// <summary>
    /// Declares that this tool changes nothing. A search reads the index it already has and
    /// returns passages; it does not write, send, or alter a thing, so a person should not have
    /// to answer a dialog for it.
    /// </summary>
    public IReadOnlyList<AgentAction> DescribeActions(AgentStep step) => [];

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(
        ToolRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = request.GetParameter("query") ?? request.Goal;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var knowledgeBase = await _knowledgeBases.GetOrCreateDefaultAsync(cancellationToken);

            // Retrieving rather than asking. This tool's job is the material; composing an answer
            // from it is a separate step, so a person can see what was found before anything is
            // written about what it means.
            var results = await _retriever.RetrieveAsync(
                KnowledgeQuery.Create(knowledgeBase.Id, query, count: 8),
                cancellationToken);

            stopwatch.Stop();

            if (results.Count == 0)
            {
                // An empty result is a success. Reporting it as a failure would stop the plan and
                // tell a person something broke, when in fact their index simply had nothing about
                // the subject — which is a different thing and has a different next step.
                _logger.LogInformation("Knowledge search found nothing for a {Length}-character query.", query.Length);

                return ToolResult.Empty(
                    Name,
                    $"Nothing in your indexed documents matched \"{Truncate(query)}\".",
                    ToolResult.Data1("count", 0));
            }

            var sources = results
                .Select(result => $"{result.FileName}#{result.Sequence}")
                .ToArray();

            return ToolResult.Success(
                Name,
                string.Join(
                    "\n\n",
                    results.Select(result => $"[{result.FileName}]\n{result.Text}")),
                sources,
                BuildData(results, query),
                stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Knowledge search failed for a {Length}-character query.", query.Length);

            return ToolResult.Failure(
                Name,
                ErrorCodes.AgentStepFailed,
                "I could not search your documents just now.");
        }
    }

    /// <summary>
    /// Builds the metadata, which names the files involved and the score range but holds no text.
    /// The highest score is included because "the closest match scored 0.3" tells somebody more
    /// about whether to trust the result than the file name alone does.
    /// </summary>
    private static IReadOnlyDictionary<string, string> BuildData(
        IReadOnlyList<KnowledgeSearchResult> results,
        string query)
    {
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["count"] = results.Count.ToString(CultureInfo.InvariantCulture),
            ["query"] = Truncate(query, 80),
            ["fileCount"] = results.Select(result => result.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                .ToString(CultureInfo.InvariantCulture),
        };

        var scored = results.Where(result => !double.IsNaN(result.CombinedScore)).ToArray();
        var best = scored.Length > 0 ? scored.Max(result => result.CombinedScore) : double.NaN;

        if (!double.IsNaN(best))
        {
            data["topScore"] = best.ToString("0.00", CultureInfo.InvariantCulture);
        }

        data["summary"] =
            $"Found {results.Count.ToString(CultureInfo.InvariantCulture)} passage(s) " +
            $"across {data["fileCount"]} file(s).";

        return data;
    }

    /// <summary>Shortens a query for a log or a label without losing where it was cut.</summary>
    private static string Truncate(string value, int maximum = 60) =>
        value.Length <= maximum ? value : string.Concat(value.AsSpan(0, maximum - 1), "\u2026");
}
