using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Core.Abstractions.Agents;

/// <summary>
/// Turns a request into a plan: which capability is needed, which tools will be used, and in
/// what order.
/// <para>
/// The planner is the only component that reads a request and decides what happens next, and it
/// produces a <see cref="AgentPlan"/> rather than performing anything. It cannot run a tool even
/// by accident, because it holds no reference to one — the registry hands it names and sentences
/// and nothing else. That is the whole reason a plan can be shown to a person, argued with, and
/// validated before a single document is opened.
/// </para>
/// <para>
/// The plan it returns is in <see cref="Core.Enums.AgentPlanStatus.Draft"/>. Only the executor
/// can move a plan to <see cref="Core.Enums.AgentPlanStatus.Ready"/>, and only after checking
/// every step against the registry. A planner that returned a runnable plan would be a way to
/// execute text from a model.
/// </para>
/// </summary>
public interface IAgentPlanner
{
    /// <summary>
    /// Works out what a request is asking for and how to do it.
    /// <para>
    /// A request that means nothing the assistant can help with is not an error. It comes back
    /// as a failure carrying <see cref="ErrorCodes.AgentIntentUnrecognized"/>, and the sentence
    /// for that says so plainly rather than inventing a plan.
    /// </para>
    /// </summary>
    /// <param name="context">The request, where it came from, and any conversation it belongs to.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    Task<Result<AgentPlan>> CreatePlanAsync(
        AgentRequestContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Produces the sentence shown to a person when a request is recognised, so they can be told
    /// what is about to happen before it happens. Kept separate from planning because the first
    /// use of a new capability is exactly the one where a person wants to know first.
    /// </summary>
    Task<string?> DescribePlanAsync(
        AgentPlan plan,
        CancellationToken cancellationToken = default);
}
