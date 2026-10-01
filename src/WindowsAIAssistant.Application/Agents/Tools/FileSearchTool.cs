using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Abstractions.Files;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Application.Agents.Tools;

/// <summary>
/// Finds files on this computer by name.
/// <para>
/// Read-only, so it declares no actions and is never held at an approval gate. A search that
/// only reads the filesystem is exactly the kind of step a person should not have to say yes to,
/// and a plan that is entirely made of these runs without a single interruption.
/// </para>
/// <para>
/// What comes back is names, paths, and dates. The tool does not open the files: a search that
/// read each match to summarize it would turn a request to find something into an unrequested
/// reading of everything it found.
/// </para>
/// </summary>
public sealed class FileSearchTool : ITool
{
    private readonly IFileSearchService _fileSearch;
    private readonly ILogger<FileSearchTool> _logger;

    public FileSearchTool(IFileSearchService fileSearch, ILogger<FileSearchTool> logger)
    {
        ArgumentNullException.ThrowIfNull(fileSearch);
        ArgumentNullException.ThrowIfNull(logger);

        _fileSearch = fileSearch;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "FileSearchTool";

    /// <inheritdoc />
    public string Description =>
        "Find files on this computer by name. Use this when someone is looking for a file they " +
        "have not indexed, or for one outside the knowledge base.";

    /// <inheritdoc />
    public IReadOnlyList<string> RequiredInputs => ["query"];

    /// <summary>
    /// Declares that this tool changes nothing. It walks directories and returns names, paths,
    /// and dates; it opens no file to read it and writes nothing anywhere.
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
            var results = await _fileSearch.SearchAsync(query, cancellationToken);
            stopwatch.Stop();

            if (results.Count == 0)
            {
                _logger.LogInformation("File search found nothing for a {Length}-character query.", query.Length);

                return ToolResult.Empty(
                    Name,
                    $"No file matched \"{Truncate(query)}\".",
                    ToolResult.Data1("count", 0));
            }

            // Ordered here rather than trusting the service's order to survive the trip, so the
            // best match is first in both what is shown and what a later step reads.
            var ordered = results
                .OrderByDescending(result => result.RelevanceScore)
                .ToArray();

            return ToolResult.Success(
                Name,
                string.Join("\n", ordered.Select(Describe)),
                ordered.Select(result => result.FullPath).ToArray(),
                BuildData(ordered, query),
                stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "File search failed for a {Length}-character query.", query.Length);

            return ToolResult.Failure(
                Name,
                ErrorCodes.AgentStepFailed,
                "I could not search your files just now.");
        }
    }

    /// <summary>Formats one result as a single line a person can read.</summary>
    private static string Describe(FileSearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return $"{result.Name} — {result.FullPath} — " +
            $"{Size(result.Size)} — {result.LastModified.ToLocalTime():d MMM yyyy}";
    }

    /// <summary>Builds the metadata, which counts and names but never contents.</summary>
    private static IReadOnlyDictionary<string, string> BuildData(
        IReadOnlyList<FileSearchResult> results,
        string query)
    {
        var extensions = results
            .Select(result => result.Extension)
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["count"] = results.Count.ToString(CultureInfo.InvariantCulture),
            ["query"] = Truncate(query, 80),
            ["summary"] = $"Found {results.Count.ToString(CultureInfo.InvariantCulture)} file(s).",
        };

        if (extensions.Length > 0)
        {
            data["types"] = string.Join(", ", extensions);
        }

        return data;
    }

    /// <summary>Renders a byte count in the units a person would say out loud.</summary>
    private static string Size(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes.ToString(CultureInfo.InvariantCulture)} B"
            : $"{value.ToString("0.#", CultureInfo.InvariantCulture)} {units[unit]}";
    }

    private static string Truncate(string value, int maximum = 60) =>
        value.Length <= maximum ? value : string.Concat(value.AsSpan(0, maximum - 1), "\u2026");
}
