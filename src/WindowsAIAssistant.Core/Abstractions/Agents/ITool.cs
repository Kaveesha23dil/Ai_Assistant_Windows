using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Core.Abstractions.Agents;

/// <summary>
/// One capability the agent can reach on the user's behalf.
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
/// <para>
/// <see cref="DescribeActions"/> has no default, and that is the most important thing on this
/// interface. A default of "no actions" is the shape of a read-only tool, so it is the shape a
/// tool that forgets to declare itself is given too — and a tool that writes a file without
/// declaring an action is not refused at the gate, it is allowed through it, silently, on the
/// strength of an omission. Making the member required turns that mistake into a compile error
/// instead of a property of the design nobody is left to check. A read-only tool writes
/// <c>DescribeActions</c> returning nothing, and that line is then a decision on the record
/// rather than an absence of one.
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
    /// <para>
    /// No default implementation, deliberately — see the remarks on <see cref="ITool"/>. There is
    /// no tool that only reads and is not allowed to say so, and no tool that changes something
    /// and can avoid saying so either.
    /// </para>
    /// </summary>
    IReadOnlyList<AgentAction> DescribeActions(AgentStep step);

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
