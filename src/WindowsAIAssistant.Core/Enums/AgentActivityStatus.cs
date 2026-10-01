namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// How an entry in the activity timeline ended.
/// <para>
/// An activity record outlives the run that produced it, so it needs to say afterwards whether
/// the thing it describes actually worked. A history that only listed what was attempted would
/// show the same entries whether the assistant had been succeeding or failing all week.
/// </para>
/// </summary>
public enum AgentActivityStatus
{
    /// <summary>Running right now.</summary>
    Running = 0,

    /// <summary>Finished and produced a result.</summary>
    Completed = 1,

    /// <summary>Attempted and did not produce a result.</summary>
    Failed = 2,

    /// <summary>Stopped because a person refused an approval.</summary>
    Rejected = 3,

    /// <summary>Stopped because the person cancelled.</summary>
    Cancelled = 4
}
