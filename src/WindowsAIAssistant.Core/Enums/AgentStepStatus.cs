namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// What happened to one step of a plan.
/// <para>
/// A step that was never reached is <see cref="Pending"/>, not <see cref="Failed"/>. The
/// difference matters to the person reading the timeline: a plan stopped at step two of five
/// has three steps nobody attempted, and showing them as failures would claim five problems
/// where there was one.
/// </para>
/// </summary>
public enum AgentStepStatus
{
    /// <summary>Not attempted yet.</summary>
    Pending = 0,

    /// <summary>Running right now.</summary>
    Running = 1,

    /// <summary>Finished and produced a result.</summary>
    Succeeded = 2,

    /// <summary>Attempted and did not produce a result.</summary>
    Failed = 3,

    /// <summary>Deliberately not attempted because the plan stopped before it.</summary>
    Skipped = 4,

    /// <summary>Held at the approval gate, waiting for a person to answer.</summary>
    AwaitingApproval = 5,

    /// <summary>The person refused the approval, so the step did not run.</summary>
    Rejected = 6
}
