namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// How far through its life a plan is.
/// <para>
/// The order is deliberate: a plan only becomes <see cref="Ready"/> once every step has been
/// checked against the tool registry, and it never becomes <see cref="Ready"/> directly from
/// <see cref="Draft"/>. That gap is the whole safety argument for a planner that reads a model's
/// output — there is no state in which a plan produced by a model is the plan that runs.
/// </para>
/// </summary>
public enum AgentPlanStatus
{
    /// <summary>Produced, not yet checked against the tools that are actually installed.</summary>
    Draft = 0,

    /// <summary>Checked: every step names a real tool and supplies what that tool requires.</summary>
    Ready = 1,

    /// <summary>Running. Steps before the current one have already been attempted.</summary>
    Executing = 2,

    /// <summary>Every step finished and produced a result.</summary>
    Completed = 3,

    /// <summary>Stopped before finishing. The step that stopped it is marked in the result.</summary>
    Failed = 4,

    /// <summary>Stopped because the person said stop, or because the request was withdrawn.</summary>
    Cancelled = 5,

    /// <summary>Stopped because a step needed an approval that was refused.</summary>
    Rejected = 6
}
