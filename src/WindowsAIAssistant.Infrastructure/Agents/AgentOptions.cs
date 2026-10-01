using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Infrastructure.Agents;

/// <summary>
/// The bound configuration for the agent layer.
/// <para>
/// Everything here changes what the agent <em>remembers</em> or how it is <em>presented</em>.
/// Nothing here changes what it is able to do: the set of tools comes from the registrations in
/// <c>AddAgents</c> and the switches come from the permission service, so there is deliberately
/// no option that could add a capability or switch a consent off from a file on disk.
/// </para>
/// <para>
/// That is the whole reason this class is small. A configuration section for an agent is an
/// attractive place to put a model name, a temperature, or a tool list, and every one of those
/// would be a way for a text file to change what the process can do.
/// </para>
/// </summary>
public sealed class AgentOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Agent";

    /// <summary>
    /// Gets the name of the agent's own SQLite file inside the per-user application data folder.
    /// <para>
    /// A name and not a path, and it is checked for separators before it is used. A path here
    /// would be a way to point somebody's preferences at a file on a network share, which is a
    /// different feature with a very different threat model.
    /// </para>
    /// </summary>
    public string DatabaseFileName { get; set; } = "agent.db";

    /// <summary>
    /// Gets or sets whether preferences and the activity timeline are written to that file.
    /// <para>
    /// Persistent by default, because the stores this selects are the ones that keep what the
    /// agent learned. <see cref="AgentPersistenceMode.SessionOnly"/> is still available and still
    /// honours every rule the persistent stores do — it simply leaves no file behind, which is the
    /// right choice for a machine being demonstrated rather than used.
    /// </para>
    /// </summary>
    public AgentPersistenceMode Persistence { get; set; } = AgentPersistenceMode.Persistent;

    /// <summary>
    /// Gets or sets how the agent workspace presents itself.
    /// <para>
    /// Competition mode changes the wording and the ordering on the workspace page and nothing
    /// else. It cannot add a tool, relax a consent switch, or pre-load an answer, so the mode
    /// cannot be used to make the assistant look more capable than the machine it is on.
    /// </para>
    /// </summary>
    public AgentPresentationMode PresentationMode { get; set; } = AgentPresentationMode.Standard;

    /// <summary>
    /// Gets or sets how many finished runs the timeline keeps. Bounded, because a store that
    /// grows without limit is a store whose contents will still be there in a year.
    /// </summary>
    public int ActivityRetentionCount { get; set; } = 200;
}