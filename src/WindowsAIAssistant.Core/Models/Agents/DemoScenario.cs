using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Agents;

/// <summary>
/// A prepared request the assistant can be run against, for a demonstration or a test.
/// <para>
/// These carry the prompt and the steps the prompt is expected to produce, and nothing else.
/// They do not carry canned answers, because a demonstration that replays a stored response is
/// not a demonstration of anything: the whole point of showing these to somebody is that the
/// planner, the tools, and the retrieval all run for real against whatever is on the machine.
/// </para>
/// <para>
/// <see cref="ExpectedTools"/> is a claim that can be checked, and is. A test asserts that the
/// planner sends a demonstration through the same path as a typed request, which is what stops
/// the showcase from quietly being a separate feature that only works when nobody is watching.
/// </para>
/// </summary>
public sealed record DemoScenario
{
    public DemoScenario(
        string id,
        string title,
        string prompt,
        string description,
        IEnumerable<string> expectedTools,
        IEnumerable<AgentCapability> capabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(expectedTools);
        ArgumentNullException.ThrowIfNull(capabilities);

        Id = id.Trim();
        Title = title.Trim();
        Prompt = prompt.Trim();
        Description = description.Trim();
        ExpectedTools = [.. expectedTools];
        Capabilities = [.. capabilities];
    }

    /// <summary>Gets the stable name of this scenario, used in tests and in a log.</summary>
    public string Id { get; }

    /// <summary>Gets the name shown on the workspace card.</summary>
    public string Title { get; }

    /// <summary>
    /// Gets the request that is sent. It is a real request in the person's own words, so the
    /// planner has to do the same work it would for anything else.
    /// </summary>
    public string Prompt { get; }

    /// <summary>Gets one sentence saying what the scenario demonstrates.</summary>
    public string Description { get; }

    /// <summary>Gets the tool names the plan is expected to choose, in order.</summary>
    public IReadOnlyList<string> ExpectedTools { get; }

    /// <summary>Gets the capabilities the scenario is meant to show off.</summary>
    public IReadOnlyList<AgentCapability> Capabilities { get; }

    /// <summary>
    /// Gets the request as a tool would receive it, tagged as a demonstration so that the
    /// activity record says the run came from here and not from a person.
    /// </summary>
    public AgentRequestContext AsContext() => AgentRequestContext.Demo(this);
}
