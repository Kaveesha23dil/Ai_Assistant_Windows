using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Application.Agents;

/// <summary>
/// Works out which capabilities this machine can actually use right now, and says why not when
/// it cannot.
/// <para>
/// The workspace is judged on this. A showcase that lists eight capabilities and then refuses
/// four of them when somebody tries them reads as a product that does not work, and the person
/// watching has no way to tell which is which. Every entry here therefore carries a reason, and
/// the reason names the switch rather than the failure — "screen analysis is switched off" is
/// something a person can act on, and "vision unavailable" is not.
/// </para>
/// <para>
/// The check is deliberately done against the same permission service and the same registry the
/// executor will use at run time. A capability that reports available and then refuses would be
/// worse than one that never claimed to be available, so the two must not be able to disagree.
/// </para>
/// </summary>
public sealed class AgentCapabilityReporter : IAgentCapabilityReporter
{
    private readonly IToolRegistry _tools;
    private readonly IPermissionService _permissions;

    public AgentCapabilityReporter(IToolRegistry tools, IPermissionService permissions)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(permissions);

        _tools = tools;
        _permissions = permissions;
    }

    /// <summary>
    /// Reports every capability, in the order they should be read, with the reason for each that
    /// cannot currently be used.
    /// </summary>
    public IReadOnlyList<AgentCapabilityStatus> Report()
    {
        var statuses = new List<AgentCapabilityStatus>(AgentCapabilities.All.Count);

        foreach (var catalogue in AgentCapabilities.All)
        {
            statuses.Add(WithAvailability(catalogue));
        }

        return statuses;
    }

    /// <summary>
    /// Reports one capability, or <see langword="null"/> when it is not one this build knows
    /// about — which is a different answer from "unavailable", because an unknown capability is
    /// not something a person can switch on.
    /// </summary>
    public AgentCapabilityStatus? Report(AgentCapability capability)
    {
        var catalogue = AgentCapabilities.All.FirstOrDefault(status => status.Capability == capability);

        return catalogue is null ? null : WithAvailability(catalogue);
    }

    /// <summary>
    /// Returns the catalogue entry marked for what this machine can actually do. The entry's
    /// title and description are kept either way: a capability that is switched off is still
    /// listed, because the point of showing it is to say which switch is off.
    /// </summary>
    private AgentCapabilityStatus WithAvailability(AgentCapabilityStatus catalogue)
    {
        var reason = ReasonUnavailable(catalogue.Capability);

        return reason is null
            ? catalogue.AsAvailable()
            : catalogue.AsUnavailable(reason);
    }

    /// <summary>
    /// Returns why a capability cannot be used, or <see langword="null"/> when it can.
    /// <para>
    /// A capability is judged by the tools behind it. A capability with no registered tool is
    /// reported as unavailable rather than as available-with-nothing-behind-it, because a person
    /// asking for a capability and getting an empty plan would conclude the request was
    /// misunderstood, when in fact the feature is simply not in this build.
    /// </para>
    /// </summary>
    private string? ReasonUnavailable(AgentCapability capability)
    {
        var tools = ToolsFor(capability);

        if (tools.Count == 0)
        {
            // Voice is the one capability with no tool behind it: the request arrives already
            // transcribed, and what it needs is the microphone, not a registered tool.
            if (capability == AgentCapability.VoiceControl)
            {
                return _permissions.IsGranted(PermissionCapability.Microphone)
                    ? null
                    : "Listening is switched off.";
            }

            return "This build does not include it.";
        }

        foreach (var tool in tools)
        {
            var reason = _tools.GetUnavailableReason(tool);

            if (reason is not null)
            {
                return reason;
            }
        }

        // A capability needs both its tools and the consent to use them. Checking the tools
        // first is deliberate: if a tool is missing, the person needs a build, not a switch
        // turned on, and telling them to change a setting would send them somewhere that
        // cannot help.
        var permission = PermissionFor(capability);

        if (permission is not null && !_permissions.IsGranted(permission.Value))
        {
            return _permissions.GetDeniedMessage(permission.Value);
        }

        return null;
    }

    /// <summary>
    /// The consent switch each capability depends on, or <see langword="null"/> when it needs
    /// none. A capability that reads only this machine's own derived state — calculation and
    /// system information — has no switch, because a person who is using the app has already
    /// decided to use the app.
    /// </summary>
    private static PermissionCapability? PermissionFor(AgentCapability capability) => capability switch
    {
        AgentCapability.VoiceControl => PermissionCapability.Microphone,
        AgentCapability.KnowledgeSearch => PermissionCapability.KnowledgeBase,
        AgentCapability.ScreenUnderstanding => PermissionCapability.ScreenAnalysis,
        AgentCapability.ReportGeneration => PermissionCapability.FileWrite,
        AgentCapability.FileSearch => PermissionCapability.FileSearch,
        AgentCapability.DocumentAnalysis => PermissionCapability.DocumentCloudProcessing,
        _ => null,
    };

    /// <summary>
    /// The tools that make up each capability. Several capabilities lean on more than one tool,
    /// and one — knowledge search — leans on both the retrieval and the composition, because a
    /// capability that can find passages but cannot answer from them is not the capability the
    /// workspace advertises.
    /// </summary>
    private static IReadOnlyList<string> ToolsFor(AgentCapability capability) => capability switch
    {
        AgentCapability.KnowledgeSearch => ["KnowledgeSearchTool", "AnswerCompositionTool"],
        AgentCapability.DocumentAnalysis => ["DocumentAnalysisTool"],
        AgentCapability.ScreenUnderstanding => ["ScreenAnalysisTool"],
        AgentCapability.ReportGeneration => ["ReportGenerationTool"],
        AgentCapability.FileSearch => ["FileSearchTool"],
        AgentCapability.SystemInformation => ["SystemInformationTool"],
        AgentCapability.Calculation => ["CalculationTool"],

        // Voice is not a tool the agent calls; it is a way a request arrives. Availability is
        // decided by the switches the voice pipeline itself honours.
        AgentCapability.VoiceControl => [],

        _ => [],
    };

    /// <summary>
    /// Counts the capabilities that are usable, for the one-line summary at the top of the
    /// workspace.
    /// </summary>
    public (int Available, int Total) Summarize()
    {
        var report = Report();

        return (report.Count(status => status.IsAvailable), report.Count);
    }
}
