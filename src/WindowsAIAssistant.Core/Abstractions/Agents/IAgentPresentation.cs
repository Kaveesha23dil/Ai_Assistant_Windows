using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Core.Abstractions.Agents;

/// <summary>
/// Answers how the agent should present itself right now.
/// <para>
/// The workspace asks this rather than reading configuration, so that a page can be written once
/// and still show the judging layout on a machine configured for it and the ordinary layout
/// everywhere else. It is an interface in Core because that is where the pages live, and it
/// carries no capability switch — there is deliberately no way to ask this interface for a tool,
/// a permission, or an answer.
/// </para>
/// <para>
/// The distinction that matters is the one in <see cref="IsCompetition"/>. That changes wording
/// and ordering and nothing else. It cannot make the assistant more capable, and a request it
/// cannot answer is still reported as unanswerable, which is the only honest version of a mode
/// meant for being judged.
/// </para>
/// </summary>
public interface IAgentPresentation
{
    /// <summary>Gets how the workspace is presenting itself.</summary>
    AgentPresentationMode Mode { get; }

    /// <summary>
    /// Gets a value indicating whether the judging layout is in effect.
    /// <para>
    /// Read by the pages to decide whether to show the showcase scenarios and the quick
    /// actions, and by nothing else. No agent service consults it.
    /// </para>
    /// </summary>
    bool IsCompetition { get; }

    /// <summary>Gets the title shown at the top of the workspace.</summary>
    string Headline { get; }

    /// <summary>Gets the sentence shown under the title.</summary>
    string Subtitle { get; }

    /// <summary>Gets the prompt shown above the request box.</summary>
    string Prompt { get; }

    /// <summary>
    /// Gets the scenarios to list, in the order they should appear.
    /// <para>
    /// Competition mode puts the three showcase scenarios first and follows them with the rest;
    /// the standard mode lists everything cheapest-first. The set is the same either way, so a
    /// scenario that exists on one machine exists on the other.
    /// </para>
    /// </summary>
    IReadOnlyList<DemoScenario> Scenarios { get; }
}