using System.Globalization;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Agents;

/// <summary>
/// One call to one tool: the name to call, the question it is answering, and whatever the
/// request was given besides those two.
/// <para>
/// The <see cref="Goal"/> is carried per step rather than only on the plan because several
/// steps of one plan legitimately ask different questions of the same tool. "Find information
/// about authentication" and "find information about session expiry" are two searches, not one
/// search repeated, and a planner that could not say that would have to choose.
/// </para>
/// <para>
/// Parameters are strings because every parameter here comes from a model or from a person, and
/// a value that arrived as text should be validated as text. A path, a count, and a format are
/// each checked by the tool that receives them, in the tool that knows what they mean.
/// </para>
/// </summary>
public sealed record ToolRequest
{
    public ToolRequest(
        string toolName,
        string goal,
        string originalRequest,
        IReadOnlyDictionary<string, string>? parameters = null,
        AgentRequestSource source = AgentRequestSource.Text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(goal);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalRequest);

        ToolName = toolName.Trim();
        Goal = goal.Trim();
        OriginalRequest = originalRequest.Trim();
        Parameters = parameters ?? EmptyParameters;
        Source = source;
    }

    /// <summary>Gets the registered name of the tool to run.</summary>
    public string ToolName { get; }

    /// <summary>Gets what this call is trying to find out or produce, in the tool's own terms.</summary>
    public string Goal { get; init; }

    /// <summary>
    /// Gets the person's request as they made it. Carried so a tool that needs the original
    /// wording — a report title, a question to put to a document — can use it without the planner
    /// having had to paraphrase it into a parameter and lose the phrasing.
    /// </summary>
    public string OriginalRequest { get; }

    /// <summary>Gets the named values supplied with this call, compared without regard to case.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; }

    /// <summary>Gets where the request entered the agent from.</summary>
    public AgentRequestSource Source { get; }

    /// <summary>
    /// Gets what earlier steps of the same run produced, gathered into one block of text; or
    /// <see langword="null"/> when this is the first step.
    /// <para>
    /// This is the only channel between steps, and it is deliberately a field of its own rather
    /// than an entry in <see cref="Parameters"/>. A planner cannot fill it — it has not run yet,
    /// so there is nothing for it to fill it with — and only the executor sets it, from results
    /// that already happened. Keeping it separate is what makes that true: a parameter named
    /// "context" arriving from a model would be indistinguishable from one the executor gathered,
    /// and the difference between those two decides what a person was about to send to a
    /// provider.
    /// </para>
    /// <para>
    /// The text is only ever what a step already returned, never the screen, never a document
    /// on disk, and never the original request. A tool that wants to reason over this material
    /// is reasoning over material the run has already decided to expose.
    /// </para>
    /// </summary>
    public string? PriorContext { get; init; }

    /// <summary>
    /// Gets the material to reason over: what earlier steps produced if there is any, otherwise
    /// a "context" or "content" parameter if one was supplied.
    /// <para>
    /// The parameter is the fallback and not the primary source. A planner can legitimately
    /// hand over context it was given in the request, but it cannot be the route by which one
    /// step sees another's output, because it never runs to find that out.
    /// </para>
    /// </summary>
    public string? GetMaterial() =>
        !string.IsNullOrWhiteSpace(PriorContext)
            ? PriorContext
            : GetParameter("context")
              ?? GetParameter("content");

    /// <summary>
    /// Reads a parameter, or returns <see langword="null"/> when it was not supplied. An empty
    /// string counts as absent: a planner that emitted <c>"query": ""</c> meant to supply
    /// nothing, and treating that as a value would send an empty search to a retriever.
    /// </summary>
    public string? GetParameter(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (Parameters.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        return null;
    }

    /// <summary>Reads a parameter that must be present, throwing when it is not.</summary>
    /// <remarks>
    /// Used only by tools whose caller the executor has already validated. A tool reached
    /// without its required parameter is a planning bug, and this says so plainly rather than
    /// reporting a person's request as invalid.
    /// </remarks>
    public string RequireParameter(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return GetParameter(name)
            ?? throw new InvalidOperationException(
                $"The {ToolName} tool was called without the required '{name}' parameter.");
    }

    /// <summary>Reads an integer parameter, falling back when it is absent or unreadable.</summary>
    public int GetIntParameter(string name, int fallback)
    {
        var value = GetParameter(name);

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    /// <summary>Returns this request with one parameter added or replaced.</summary>
    public ToolRequest WithParameter(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var updated = new Dictionary<string, string>(Parameters, StringComparer.OrdinalIgnoreCase)
        {
            [name] = value,
        };

        return this with { Parameters = updated };
    }

    /// <summary>Returns this request with its goal replaced.</summary>
    public ToolRequest WithGoal(string goal) => this with { Goal = goal.Trim() };

    private static IReadOnlyDictionary<string, string> EmptyParameters { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
