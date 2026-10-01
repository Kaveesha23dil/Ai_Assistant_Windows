using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Agents;

/// <summary>
/// Everything one run of the agent needs that is not the plan itself: the original words, the
/// results of earlier steps, and the answers a person has already given.
/// <para>
/// Later steps read earlier results through <see cref="TryGetResult"/>. That is the whole
/// mechanism by which "search, then summarize what was found" works: step three does not need to
/// know that step one ran, only that something called a knowledge search left a result behind
/// under a key both of them can name.
/// </para>
/// <para>
/// The store is concurrent and lives for one run. It is not a conversation: nothing here
/// survives the request, and a plan cannot read a previous plan's documents. That is deliberate,
/// because a run that could reach into an earlier one would be a second way for content to
/// accumulate on a machine where the person never asked for it to.
/// </para>
/// </summary>
public sealed record AgentExecutionContext
{
    private readonly ConcurrentDictionary<string, ToolResult> _results =
        new(StringComparer.OrdinalIgnoreCase);

    public AgentExecutionContext(
        Guid runId,
        string request,
        AgentRequestSource source = AgentRequestSource.Text,
        Guid? conversationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request);

        RunId = runId;
        Request = request.Trim();
        Source = source;
        ConversationId = conversationId;
    }

    /// <summary>Gets the identifier of this run, shared by the plan, the timeline, and the memory record.</summary>
    public Guid RunId { get; }

    /// <summary>Gets the person's request, exactly as it was made.</summary>
    public string Request { get; init; }

    /// <summary>Gets where the request entered the agent from.</summary>
    public AgentRequestSource Source { get; }

    /// <summary>Gets the conversation this request belongs to, when it belongs to one.</summary>
    public Guid? ConversationId { get; init; }

    /// <summary>
    /// Gets the tool names this run has already called, in order. Shown in the timeline so a
    /// person can see which capabilities were used without reading the plan.
    /// </summary>
    public IReadOnlyList<string> ToolsUsed => _results.Values
        .OrderBy(ReadOrder)
        .Select(result => result.ToolName)
        .ToArray();

    /// <summary>
    /// Stores a result under a key that later steps can read. The executor is the only caller:
    /// the key is derived from the step's position and tool, so it cannot be guessed to
    /// overwrite another step's output.
    /// </summary>
    public void RecordResult(string key, int order, ToolResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(result);

        // The order is kept inside the result's own metadata rather than in a second collection,
        // so reading a result back hands over the very object that was stored rather than a
        // projection of it.
        _results[key] = result with
        {
            Data = new Dictionary<string, string>(result.Data, StringComparer.OrdinalIgnoreCase)
            {
                [OrderKey] = order.ToString(CultureInfo.InvariantCulture),
            },
        };
    }

    /// <summary>Reads a result an earlier step left behind.</summary>
    public bool TryGetResult(string key, out ToolResult? result)
    {
        if (!string.IsNullOrWhiteSpace(key) && _results.TryGetValue(key, out var found))
        {
            result = found;
            return true;
        }

        result = null;
        return false;
    }

    /// <summary>
    /// Reads the content of the first earlier result that has any, ignoring steps that found
    /// nothing. This is what lets "generate a summary" work whether the search before it returned
    /// three passages or none at all, without the planner having to name a key it may not get.
    /// </summary>
    public bool TryGetFirstContent(out string? content, out string? toolName)
    {
        foreach (var result in _results.Values.OrderBy(ReadOrder))
        {
            if (result.HasContent)
            {
                content = result.Content;
                toolName = result.ToolName;
                return true;
            }
        }

        content = null;
        toolName = null;
        return false;
    }

    /// <summary>Collects the sources reported by every earlier step, in order and without repeats.</summary>
    public IReadOnlyList<string> CollectSources() =>
        _results.Values
            .OrderBy(ReadOrder)
            .SelectMany(result => result.Sources)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>Returns this context with the conversation it belongs to filled in.</summary>
    public AgentExecutionContext WithConversation(Guid conversationId) =>
        this with { ConversationId = conversationId };

    /// <summary>Returns this context with its request replaced, for a step that refines the wording.</summary>
    public AgentExecutionContext WithRequest(string request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request);
        return this with { Request = request.Trim() };
    }

    /// <summary>
    /// Builds the text handed to a model when a step needs to reason over what earlier steps
    /// found. Kept in Core so the report and summary tools present their material identically,
    /// and so the privacy rule — never the screen, never a document, only what a step produced —
    /// is written down once.
    /// <para>
    /// Each block is prefixed with the tool that produced it when more than one tool contributed,
    /// so a model can attribute an answer to where it came from. With a single contributor the
    /// prefix is left off, because a tool that writes its material to a file would otherwise
    /// write the agent's own bookkeeping into the document.
    /// </para>
    /// </summary>
    public string BuildContextText(int maximumCharacters = 8000)
    {
        var contributing = _results.Values
            .OrderBy(ReadOrder)
            .Where(result => result.HasContent)
            .ToArray();

        // With one contributor there is nothing to attribute, so the text is passed on as it
        // came. That matters for the tools that write what they are given to a file: a report
        // built from one search would otherwise open with a line reading "[KnowledgeSearchTool]"
        // that belongs to the agent's own plumbing rather than to anything the reader asked for.
        var label = contributing.Length > 1;

        var builder = new StringBuilder();
        var used = 0;

        foreach (var result in contributing)
        {
            if (used >= maximumCharacters)
            {
                break;
            }

            var room = maximumCharacters - used;
            var text = result.Content!.Length > room
                ? result.Content[..room]
                : result.Content;

            if (label)
            {
                builder.AppendLine($"[{result.ToolName}]");
            }

            builder.AppendLine(text);
            builder.AppendLine();
            used += text.Length;
        }

        return builder.ToString().Trim();
    }

    private static int ReadOrder(ToolResult result) =>
        result.Data.TryGetValue(OrderKey, out var order)
        && int.TryParse(order, out var parsed)
            ? parsed
            : int.MaxValue;

    /// <summary>
    /// The metadata key the executor stamps each result with. Named here rather than written as a
    /// literal at the two places that use it, so a rename cannot leave the reader and the writer
    /// disagreeing about where the order lives.
    /// </summary>
    private const string OrderKey = "order";
}
