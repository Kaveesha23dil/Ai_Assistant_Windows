namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// What kind of thing a memory holds.
/// <para>
/// The type is part of the value rather than a label on a side table because the rules about
/// what may be remembered differ per kind. A preference is written because somebody asked for it;
/// a workflow note exists to save repeating a question; and neither is ever a place for the
/// content of a document or a screenshot, which is what
/// <see cref="AgentMemoryRules"/> enforces on the way in rather than trusting every call site to
/// remember.
/// </para>
/// </summary>
public enum AgentMemoryType
{
    /// <summary>Something the person told the assistant they want, such as a preferred report format.</summary>
    UserPreference = 0,

    /// <summary>
    /// A setting the person gave the assistant itself, such as "always summarize in three
    /// bullets". Distinguished from <see cref="UserPreference"/> because it is about the
    /// assistant's behaviour and survives a change of person on the same machine.
    /// </summary>
    AssistantPreference = 1,

    /// <summary>
    /// A note that a piece of work was done recently, so the assistant can say "you asked for
    /// this yesterday" without keeping what it was about.
    /// </summary>
    WorkflowHistory = 2
}
