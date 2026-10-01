using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Application.Agents;

/// <summary>
/// Decides how the agent workspace presents itself, and which scenarios it lists.
/// <para>
/// This is the whole of "competition mode", and the smallness of it is the point. It chooses a
/// headline, an order for a list of prompts, and nothing else. Every scenario it lists still runs
/// the real planner against the real registry against whatever is on the machine, and a request
/// that cannot be answered is still answered with a sentence saying so.
/// </para>
/// <para>
/// The alternative — a mode that pre-loads answers, or that marks a capability available when
/// the switch is off — would be easier to demonstrate and would be worth nothing. A judge who
/// asks a question the assistant cannot answer is going to find out, and a build that had been
/// quietly promising otherwise would be worse than one that said "that needs the knowledge base
/// switched on".
/// </para>
/// <para>
/// Scenarios are read from <see cref="AgentDemoScenarios"/> rather than duplicated here, so
/// there is one list and a scenario added for the showcase is also available in the ordinary
/// workspace.
/// </para>
/// </summary>
public sealed class AgentPresentation : IAgentPresentation
{
    private readonly AgentPresentationMode _mode;

    public AgentPresentation(AgentPresentationMode mode = AgentPresentationMode.Standard)
    {
        _mode = mode;
        Scenarios = BuildScenarios(mode);
    }

    /// <inheritdoc />
    public AgentPresentationMode Mode => _mode;

    /// <inheritdoc />
    public bool IsCompetition => _mode == AgentPresentationMode.Competition;

    /// <inheritdoc />
    public string Headline => IsCompetition ? "AI Productivity Agent" : "Agent workspace";

    /// <inheritdoc />
    public string Subtitle => IsCompetition
        ? "Every demonstration below runs for real on this machine: the planner reads the request, " +
          "the registry decides what may be called, and anything that would change a file asks first."
        : "Ask for something and watch it planned, step by step. Anything that would change a file asks first.";

    /// <inheritdoc />
    public string Prompt => IsCompetition ? "What can I help you with?" : "Ask for something";

    /// <inheritdoc />
    public IReadOnlyList<DemoScenario> Scenarios { get; }

    /// <summary>
    /// Orders the scenarios for the mode.
    /// <para>
    /// The showcase scenarios first in competition mode, then everything else in the order the
    /// standard list uses. The set is identical in both modes; only the order differs, so nothing
    /// is hidden from somebody who switches modes off.
    /// </para>
    /// </summary>
    private static IReadOnlyList<DemoScenario> BuildScenarios(AgentPresentationMode mode)
    {
        if (mode != AgentPresentationMode.Competition)
        {
            return AgentDemoScenarios.All;
        }

        var showcaseIds = AgentDemoScenarios.Showcase
            .Select(scenario => scenario.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return
        [
            .. AgentDemoScenarios.Showcase,
            .. AgentDemoScenarios.All.Where(scenario => !showcaseIds.Contains(scenario.Id)),
        ];
    }
}