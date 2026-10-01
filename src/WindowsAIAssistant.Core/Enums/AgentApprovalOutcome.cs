namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// How a person answered an approval request.
/// <para>
/// <see cref="Modified"/> exists because refusing a plan outright is not the only reaction to a
/// request a person did not ask for. Somebody who is shown "I want to write a file to your
/// Documents folder" frequently wants the report, just somewhere else, and forcing them to
/// choose between all of it and none of it is a worse answer than asking what to change.
/// </para>
/// </summary>
public enum AgentApprovalOutcome
{
    /// <summary>Run the step as it was described.</summary>
    Approved = 0,

    /// <summary>Do not run the step, and stop the plan.</summary>
    Rejected = 1,

    /// <summary>Run the step with the changes the person described, when they could be applied.</summary>
    Modified = 2,

    /// <summary>Nobody answered. Treated exactly as a refusal.</summary>
    TimedOut = 3,

    /// <summary>
    /// The run was stopped while the prompt was up. Refuses execution like the other three, but
    /// is reported separately so a person who pressed stop is not told they declined something.
    /// </summary>
    Cancelled = 4
}
