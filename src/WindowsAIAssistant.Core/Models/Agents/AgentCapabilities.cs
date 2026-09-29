using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Agents;

/// <summary>
/// One thing the assistant says it can do, and whether this machine can actually do it.
/// <para>
/// The distinction the workspace makes is between "the feature exists" and "you may use it right
/// now". A build with screen understanding in it, on a machine where the person has not turned
/// screen capture on, is a build that can explain an error message the day it is allowed to. Both
/// facts are shown, because a capability listed as unavailable with no explanation reads as a
/// broken product, and one listed as available that then refuses is worse.
/// </para>
/// </summary>
public sealed record AgentCapabilityStatus
{
    public AgentCapabilityStatus(
        AgentCapability capability,
        bool isAvailable,
        string title,
        string description,
        string? unavailableReason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Capability = capability;
        IsAvailable = isAvailable;
        Title = title.Trim();
        Description = description.Trim();
        UnavailableReason = string.IsNullOrWhiteSpace(unavailableReason) ? null : unavailableReason.Trim();
    }

    /// <summary>Gets which capability this is.</summary>
    public AgentCapability Capability { get; }

    /// <summary>Gets a value indicating whether it can be used right now.</summary>
    public bool IsAvailable { get; init; }

    /// <summary>Gets the name shown to a person.</summary>
    public string Title { get; }

    /// <summary>Gets one sentence saying what the capability is for.</summary>
    public string Description { get; }

    /// <summary>Gets why it is unavailable, or <see langword="null"/> when it is available.</summary>
    public string? UnavailableReason { get; init; }

    /// <summary>Gets the mark the workspace shows beside the capability.</summary>
    public string Mark => IsAvailable ? "\u2713" : "\u2014";

    /// <summary>Returns this capability as available.</summary>
    public AgentCapabilityStatus AsAvailable() => this with
    {
        IsAvailable = true,
        UnavailableReason = null,
    };

    /// <summary>Returns this capability as unavailable, with the reason shown beside it.</summary>
    public AgentCapabilityStatus AsUnavailable(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return this with { IsAvailable = false, UnavailableReason = reason.Trim() };
    }

    /// <summary>Builds an available capability.</summary>
    public static AgentCapabilityStatus Available(AgentCapability capability) =>
        new(capability, true, AgentCapabilities.Titles.For(capability), AgentCapabilities.Descriptions.For(capability));
}

/// <summary>
/// Every capability the agent knows about, in the order the workspace lists them.
/// <para>
/// Held in Core so the order, the names, and the descriptions are the same whether they are
/// being shown in the workspace, spoken by the voice path, or read by a test. A capability that
/// appeared in two lists with two spellings would be one nobody could find.
/// </para>
/// </summary>
public static class AgentCapabilities
{
    /// <summary>Gets the capabilities, in the order a person should read them.</summary>
    public static IReadOnlyList<AgentCapabilityStatus> All { get; } =
    [
        AgentCapabilityStatus.Available(AgentCapability.DocumentAnalysis),
        AgentCapabilityStatus.Available(AgentCapability.KnowledgeSearch),
        AgentCapabilityStatus.Available(AgentCapability.ScreenUnderstanding),
        AgentCapabilityStatus.Available(AgentCapability.VoiceControl),
        AgentCapabilityStatus.Available(AgentCapability.ReportGeneration),
        AgentCapabilityStatus.Available(AgentCapability.FileSearch),
        AgentCapabilityStatus.Available(AgentCapability.SystemInformation),
        AgentCapabilityStatus.Available(AgentCapability.Calculation),
    ];

    /// <summary>Finds one capability's default entry.</summary>
    public static AgentCapabilityStatus For(AgentCapability capability) =>
        All.FirstOrDefault(status => status.Capability == capability)
        ?? AgentCapabilityStatus.Available(capability);

    /// <summary>Gets the display names.</summary>
    public static class Titles
    {
        public static string For(AgentCapability capability) => capability switch
        {
            AgentCapability.DocumentAnalysis => "Document Analysis",
            AgentCapability.KnowledgeSearch => "Knowledge Search",
            AgentCapability.ScreenUnderstanding => "Screen Understanding",
            AgentCapability.VoiceControl => "Voice Control",
            AgentCapability.ReportGeneration => "Report Generation",
            AgentCapability.FileSearch => "File Search",
            AgentCapability.SystemInformation => "System Information",
            AgentCapability.Calculation => "Calculation",
            _ => "Unknown capability",
        };
    }

    /// <summary>Gets the one-sentence explanations shown under each name.</summary>
    public static class Descriptions
    {
        public static string For(AgentCapability capability) => capability switch
        {
            AgentCapability.DocumentAnalysis =>
                "Read a PDF, Word, PowerPoint, or spreadsheet file and summarize it.",
            AgentCapability.KnowledgeSearch =>
                "Search everything you have indexed and answer from your own documents.",
            AgentCapability.ScreenUnderstanding =>
                "Look at the screen and explain an error, a dialog, or a chart.",
            AgentCapability.VoiceControl =>
                "Take a spoken request, plan the work, and answer out loud.",
            AgentCapability.ReportGeneration =>
                "Write the result to a PDF, Word, Markdown, or text file.",
            AgentCapability.FileSearch =>
                "Find files by name on this computer.",
            AgentCapability.SystemInformation =>
                "Report what this computer is and how it is doing.",
            AgentCapability.Calculation =>
                "Work out arithmetic exactly, without asking a model.",
            _ => string.Empty,
        };
    }
}
