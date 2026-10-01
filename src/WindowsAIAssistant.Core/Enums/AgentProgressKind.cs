namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// What an <see cref="Models.Agents.AgentProgress"/> report is about.
/// <para>
/// The status alone is not enough to tell these apart, and guessing is what produces an
/// interface that shows a finished step as a new one. A run's last step and the run itself both
/// end up <see cref="AgentStepStatus.Succeeded"/>, and a step that finished and a step that is
/// about to be asked about are told apart by nothing at all. So the kind is stated outright
/// rather than inferred, and an interface is left with one reading of each report.
/// </para>
/// </summary>
public enum AgentProgressKind
{
    /// <summary>The plan has been built and checked, and is about to be run.</summary>
    PlanReady,

    /// <summary>A step has begun.</summary>
    StepStarted,

    /// <summary>A step has ended, one way or another.</summary>
    StepFinished,

    /// <summary>The run is stopped, waiting for a person to answer.</summary>
    AwaitingApproval,

    /// <summary>The run has ended.</summary>
    Completed,
}
