namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// Where a request entered the agent from.
/// <para>
/// Carried through planning, execution, the activity timeline, and the memory record, because
/// "find my project documents" said aloud and the same words typed are not the same decision:
/// the spoken one went through a recogniser that can mishear, and a summary written for a
/// person who cannot see the screen needs different wording from one written for a person who can.
/// </para>
/// </summary>
public enum AgentRequestSource
{
    /// <summary>Typed into the workspace or the chat page.</summary>
    Text = 0,

    /// <summary>Spoken, through the voice pipeline.</summary>
    Voice = 1,

    /// <summary>One of the built-in demonstration scenarios.</summary>
    DemoScenario = 2,

    /// <summary>A quick action on the workspace, such as "Analyze Screen".</summary>
    QuickAction = 3
}
