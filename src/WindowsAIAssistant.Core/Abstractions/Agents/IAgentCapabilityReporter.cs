using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Core.Abstractions.Agents;

/// <summary>
/// Reports what this machine can do right now, and why not when it cannot.
/// <para>
/// A separate interface from the registry because the two answer different questions. The
/// registry answers "is this tool callable", which is a mechanical fact about a lookup, and it
/// is consulted during planning. This one answers "what can a person ask for and expect to
/// work", which is a statement about the product, and it is consulted when the workspace is
/// being drawn. Keeping them apart is what lets the interface layer show a capability list
/// without knowing anything about tools.
/// </para>
/// </summary>
public interface IAgentCapabilityReporter
{
    /// <summary>
    /// Reports every capability in a fixed order, each with a reason when it cannot be used.
    /// </summary>
    IReadOnlyList<AgentCapabilityStatus> Report();

    /// <summary>Reports one capability, or null when this build does not include it at all.</summary>
    AgentCapabilityStatus? Report(AgentCapability capability);

    /// <summary>Counts what is usable, for a one-line summary. The first number is available.</summary>
    (int Available, int Total) Summarize();
}
