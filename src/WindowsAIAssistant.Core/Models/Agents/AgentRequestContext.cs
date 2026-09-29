using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Agents;

/// <summary>
/// One request as it arrived, before anything decided what to do with it.
/// <para>
/// The source and the scenario travel with the request rather than being looked up later. That
/// matters for two reasons: a demonstration run is labelled as one in the timeline, and a spoken
/// request is given a plan a person can hear, so the plan text is built for listening rather
/// than for scanning.
/// </para>
/// </summary>
public sealed record AgentRequestContext
{
    public AgentRequestContext(
        string request,
        AgentRequestSource source,
        Guid? conversationId = null,
        DemoScenario? scenario = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request);

        Request = request.Trim();
        Source = source;
        ConversationId = conversationId;
        Scenario = scenario;
    }

    /// <summary>Gets the request, as it was made.</summary>
    public string Request { get; }

    /// <summary>Gets where it came from.</summary>
    public AgentRequestSource Source { get; }

    /// <summary>Gets the conversation it belongs to, when it belongs to one.</summary>
    public Guid? ConversationId { get; init; }

    /// <summary>Gets the demonstration this came from, when it came from one.</summary>
    public DemoScenario? Scenario { get; }

    /// <summary>Gets a value indicating whether a person typed or spoke this.</summary>
    public bool IsFromPerson => Source is AgentRequestSource.Text or AgentRequestSource.Voice;

    /// <summary>Gets a value indicating whether the request came from the microphone.</summary>
    public bool WasSpoken => Source == AgentRequestSource.Voice;

    /// <summary>Creates a request a person typed.</summary>
    public static AgentRequestContext Typed(string request, Guid? conversationId = null) =>
        new(request, AgentRequestSource.Text, conversationId);

    /// <summary>Creates a request a person spoke.</summary>
    public static AgentRequestContext Spoken(string request, Guid? conversationId = null) =>
        new(request, AgentRequestSource.Voice, conversationId);

    /// <summary>Creates a request from one of the demonstration scenarios.</summary>
    public static AgentRequestContext Demo(DemoScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        return new AgentRequestContext(scenario.Prompt, AgentRequestSource.DemoScenario, scenario: scenario);
    }

    /// <summary>Creates a request from a workspace quick action.</summary>
    public static AgentRequestContext QuickAction(string request) =>
        new(request, AgentRequestSource.QuickAction);

    /// <summary>Returns this request attached to a conversation.</summary>
    public AgentRequestContext WithConversation(Guid conversationId) =>
        this with { ConversationId = conversationId };
}

/// <summary>
/// A short list of the things worth asking for, shown under the composer on the workspace.
/// <para>
/// They are prompts rather than commands, written the way a person would ask. That is the point:
/// a list of tool names as suggestions would teach a person the vocabulary of the
/// implementation, and the vocabulary is meant to stay an implementation detail.
/// </para>
/// </summary>
public static class AgentSuggestions
{
    /// <summary>Gets the four suggestions the workspace offers.</summary>
    public static IReadOnlyList<string> Default { get; } =
    [
        "Search my knowledge base for what I know about authentication",
        "Explain the error on my current screen",
        "Create a weekly project report from my documents",
        "Find my project documents",
    ];
}
