using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Core.Abstractions.Agents;

/// <summary>
/// The set of tools the agent may use, and the only place a tool name is turned into a tool.
/// <para>
/// A plan names tools. Nothing else does. The registry is therefore the boundary between text a
/// model produced and a capability the process actually has: a name that is not in here cannot
/// be run, whatever produced it, and a lookup is a dictionary read rather than a search of the
/// loaded types. That is what stops a planner from naming something the assistant can do but
/// was never told about, and what stops it from naming something nobody can do at all.
/// </para>
/// <para>
/// Availability is reported separately from existence, and deliberately. A tool that is
/// registered but switched off is a tool somebody can turn on, and telling a person it does not
/// exist would send them looking for a feature that is present.
/// </para>
/// </summary>
public interface IToolRegistry
{
    /// <summary>Gets the tools that are registered, in a stable order.</summary>
    IReadOnlyList<ITool> Tools { get; }

    /// <summary>Gets the names of the registered tools, in a stable order.</summary>
    IReadOnlyList<string> Names { get; }

    /// <summary>Finds a tool by name, ignoring case. Returns <see langword="false"/> for an unknown name.</summary>
    bool TryGet(string name, out ITool? tool);

    /// <summary>
    /// Reports what the registry can say about a name without running anything: whether it is
    /// registered, whether it could run right now, and why not when it could not.
    /// </summary>
    ToolAvailability Check(string name);

    /// <summary>Gets the sentence explaining why a tool is not available, when it is not.</summary>
    string? GetUnavailableReason(string name);

    /// <summary>
    /// Returns the name as the registry knows it, so a plan can be written in any case and still
    /// match. Used when a plan is built rather than run, so that everything downstream compares
    /// one spelling instead of several.
    /// </summary>
    string Resolve(string name);

    /// <summary>
    /// Lists the required inputs a tool is not being given, ignoring case and treating an empty
    /// value as absent.
    /// <para>
    /// Checked by the planner as well as the executor, so a plan that could never run is
    /// rejected while it is being written rather than part-way through a demonstration.
    /// </para>
    /// </summary>
    IReadOnlyList<string> GetMissingInputs(string name, IReadOnlyDictionary<string, string> parameters);

    /// <summary>
    /// Gets the tools as the planner should be told about them: a name, and a sentence saying
    /// what it is for. This is the whole vocabulary a plan may be written from, and it is
    /// produced from the registry rather than from a constant elsewhere, so a tool cannot be
    /// missing from the list a planner sees while still being callable.
    /// </summary>
    string BuildToolCatalogue();
}
