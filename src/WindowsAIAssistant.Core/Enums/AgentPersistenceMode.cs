namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// Whether the agent's own records survive a restart.
/// <para>
/// Exists because this is a decision somebody should make deliberately rather than a default
/// that gets argued about later. A demonstration on a judge's laptop should not leave a file
/// behind that says what was asked for; somebody who uses the assistant every day should not
/// have to re-explain a preference after closing it.
/// </para>
/// <para>
/// The choice is between two stores and nothing else. It does not change what may be recorded —
/// the rules in <c>AgentMemoryRules</c> and the shape of <c>AgentActivity</c> are identical
/// either way, and a value that would be refused in one store is refused in the other before it
/// reaches a store at all.
/// </para>
/// </summary>
public enum AgentPersistenceMode
{
    /// <summary>
    /// Records live for this session only and are discarded on exit. The default, because
    /// writing a file nobody asked for is the harder mistake to undo than forgetting something.
    /// </summary>
    SessionOnly = 0,

    /// <summary>
    /// Records are written to the agent's local SQLite file and read back on the next launch.
    /// </summary>
    Persistent = 1,
}