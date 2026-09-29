using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Core.Abstractions.Agents;

/// <summary>
/// One capability the agent can reach on the user's behalf.
/// <para>
/// The defaults for <see cref="RequiredPermission"/>, <see cref="DescribeActions"/>, and
/// <see cref="TryApplyModification"/> together describe a tool that only reads: no consent
/// switch, no action to approve, and nothing to modify. That is right for most tools, and it is
/// the safe direction to be wrong in — a tool that writes a file and forgets to say so is
/// invisible to the gate, so anything that changes something has to opt in to being asked about.
/// </para>
/// <para>
/// A tool is the whole of what the agent can actually do, and it is deliberately small: a name
/// the planner may use, a sentence describing what it is for, and one method that runs it. A
/// tool does not know about plans, steps, approval, or the other tools, and it cannot reach any
/// tool but itself — which is what makes "the planner cannot execute anything directly" a
/// property of the type system rather than a rule somebody has to remember.
/// </para>
/// <para>
/// A tool answers a refusal as a <see cref="ToolResult"/> carrying a code and a sentence, rather
/// than by throwing. It must not execute a command line, run code produced by a model, or reach
/// a document, a screen, or a network without going through the service that owns that
/// decision.
/// </para>
/// </summary>
public interface ITool
{
    /// <summary>
    /// Gets the stable name the planner names this tool by, for example
    /// <c>KnowledgeSearchTool</c>. Compared without regard to case, and never taken from
    /// anything a model produced.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets one sentence saying what the tool is for, written for the planner to read when it
    /// chooses between tools. It describes the outcome, never the implementation, so that
    /// swapping a tool's internals does not change any plan.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets what the tool needs before it can run, or an empty list when it needs nothing.
    /// <para>
    /// Named parameters, checked before the tool is called. A tool that listed nothing and then
    /// refused a missing input would make the check the tool's problem, and a planner that could
    /// not see the requirement would produce plans that fail for reasons the person cannot act
    /// on.
    /// </para>
    /// </summary>
    IReadOnlyList<string> RequiredInputs { get; }

    /// <summary>
    /// Gets the consent switch the tool depends on, or <see langword="null"/> when it needs
    /// none.
    /// <para>
    /// The same switch the voice path and the pages check, so there is one answer to "may a
    /// screenshot be sent" rather than one per caller. A tool that reached a screen or a network
    /// without naming a switch here would be the one place the privacy promise could be broken
    /// while every other caller honoured it.
    /// </para>
    /// </summary>
    PermissionCapability? RequiredPermission => null;

    /// <summary>
    /// Lists what this tool is about to do, as concrete actions, given the step that is calling
    /// it. An empty list means the tool only reads, and the executor will not interrupt a person
    /// to ask about it.
    /// <para>
    /// Asked before the tool runs, on the concrete step, so the description can name the actual
    /// file and folder rather than a generic "writes a report". This is the only way a tool can
    /// say what it will do: it is not given the chance to describe itself after the fact, and it
    /// cannot run and then report.
    /// </para>
    /// </summary>
    IReadOnlyList<AgentAction> DescribeActions(AgentStep step) => [];

    /// <summary>
    /// Folds a person's modification into a step, when the tool understands it.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the change cannot be applied, in which case the caller
    /// refuses rather than running the step as it was. Silently ignoring a modification would let
    /// a person believe they got what they asked for.
    /// </returns>
    bool TryApplyModification(AgentStep step, string modification, out AgentStep modified)
    {
        ArgumentNullException.ThrowIfNull(step);
        modified = step;
        return false;
    }

    /// <summary>
    /// Runs the tool.
    /// <para>
    /// Implementations must not throw for anything a person can act on. A refusal, an empty
    /// answer, and a fault are all returned as a <see cref="ToolResult"/> carrying a code and a
    /// sentence fit to show, so the executor can decide what to do next without parsing English.
    /// </para>
    /// </summary>
    Task<ToolResult> ExecuteAsync(
        ToolRequest request,
        CancellationToken cancellationToken = default);
}
