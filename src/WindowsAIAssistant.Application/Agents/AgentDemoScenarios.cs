using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Application.Agents;

/// <summary>
/// The six demonstrations the workspace offers, and the rules for what may be shown in one.
/// <para>
/// Each scenario is a prompt in a person's own words and the tools it is expected to choose.
/// There are no stored answers, because a demonstration that replays a recorded response is
/// not a demonstration of the thing being demonstrated: the planner, the registry, the
/// retrieval, and the consent checks all have to run for real, against whatever is on the
/// machine, or the showcase is a video with buttons.
/// </para>
/// <para>
/// That constraint is also what makes the mode honest. A scenario cannot promise an answer
/// because it does not have one to promise — it promises which tools will be involved, and the
/// pipeline either produces a real result or reports honestly that the material was not there.
/// </para>
/// <para>
/// The list is ordered from cheapest and most certain to most demanding: a scenario that needs a
/// cloud provider or a captured screen will fail on a machine with those switched off, and
/// putting it first would make the whole mode look broken on first run.
/// </para>
/// </summary>
public static class AgentDemoScenarios
{
    /// <summary>Gets the demonstrations, in the order the workspace lists them.</summary>
    public static IReadOnlyList<DemoScenario> All { get; } =
    [
        new(
            "calculate",
            "Do the arithmetic exactly",
            "What is 17 times 23",
            "Shows a plan being built and run with no model involved at all, because arithmetic is done locally and exactly.",
            ["CalculationTool"],
            [AgentCapability.Calculation]),

        new(
            "system-info",
            "Report this computer",
            "Show me my system information",
            "Reads the machine directly, with nothing leaving it and no consent needed.",
            ["SystemInformationTool"],
            [AgentCapability.SystemInformation]),

        new(
            "file-search",
            "Find a file by name",
            "Find my project documents",
            "Searches the disk and returns names, paths, and dates without opening anything.",
            ["FileSearchTool"],
            [AgentCapability.FileSearch]),

        new(
            "knowledge-search",
            "Search your own documents",
            "Search my knowledge base for what I know about authentication",
            "Retrieves passages from the local index and answers from them, citing which files were used.",
            ["KnowledgeSearchTool", "AnswerCompositionTool"],
            [AgentCapability.KnowledgeSearch]),

        new(
            "report",
            "Write a report, after asking",
            "Create a weekly project report from my documents",
            "Searches, then asks before writing a single file. Refusing the prompt stops the run with nothing written.",
            ["KnowledgeSearchTool", "ReportGenerationTool"],
            [AgentCapability.KnowledgeSearch, AgentCapability.ReportGeneration]),

        new(
            "screen",
            "Explain what is on the screen",
            "Explain the error on my current screen",
            "Captures the screen and explains it, honouring the screen switch and never keeping the image.",
            ["ScreenAnalysisTool"],
            [AgentCapability.ScreenUnderstanding]),
    ];

    /// <summary>
    /// Gets the three scenarios the judging layout lists first, in the order they build on each
    /// other.
    /// <para>
    /// Chosen for what a viewer can conclude from seeing one run each: knowledge answered from
    /// the person's own documents, a screen read and explained, and a file written only after
    /// being asked. Together they cover retrieval, a capability that cannot work without consent,
    /// and the consent gate — which is the three things that are hardest to claim and easiest to
    /// show.
    /// </para>
    /// <para>
    /// Every one of them is also in <see cref="All"/>. Nothing here is a special demonstration
    /// path; it is the same prompt going through the same planner, listed earlier.
    /// </para>
    /// </summary>
    public static IReadOnlyList<DemoScenario> Showcase { get; } =
    [
        new(
            "knowledge-assistant",
            "Knowledge Assistant",
            "Find all information about deployment architecture",
            "Searches the documents you have indexed, retrieves the passages that match, and writes the summary from them — naming which files it used.",
            ["KnowledgeSearchTool", "AnswerCompositionTool"],
            [AgentCapability.KnowledgeSearch]),

        new(
            "developer-assistant",
            "Developer Assistant",
            "Explain this error",
            "Captures the screen, reads the message out of it, and explains it. Honours the screen switch, and the image is never written down.",
            ["ScreenAnalysisTool"],
            [AgentCapability.ScreenUnderstanding]),

        new(
            "report-generator",
            "Report Generator",
            "Create weekly project report",
            "Searches first, then asks before it writes anything. Refusing the prompt stops the run with no file created.",
            ["KnowledgeSearchTool", "ReportGenerationTool"],
            [AgentCapability.KnowledgeSearch, AgentCapability.ReportGeneration]),
    ];

    /// <summary>Finds a scenario by its identifier, ignoring case, across every list.</summary>
    public static DemoScenario? Find(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : Showcase.Concat(All).FirstOrDefault(scenario =>
                string.Equals(scenario.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reports whether a scenario's tools are all available right now, and which are not.
    /// <para>
    /// Used to mark a card before it is pressed rather than to refuse it afterwards. A scenario
    /// somebody can see is unavailable, with the reason beside it, teaches them what the switch
    /// is; one that fails when pressed teaches them nothing except that the app is unreliable.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> UnavailableTools(
        DemoScenario scenario,
        Func<string, bool> isAvailable)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(isAvailable);

        return scenario.ExpectedTools
            .Where(tool => !isAvailable(tool))
            .ToArray();
    }

    /// <summary>
    /// Gets the four things the judging layout offers as one-click starts, in the order they are
    /// shown.
    /// <para>
    /// Each is an ordinary prompt in the workspace's own request box, not a shortcut around the
    /// agent: pressing one fills the box and runs the same path a person typing the same words
    /// would take. Four rather than eight, because a page offering every capability is a page
    /// nobody reads, and these four cover retrieval, screen, writing, and the workflow the other
    /// three compose into.
    /// </para>
    /// </summary>
    public static IReadOnlyList<AgentQuickAction> QuickActions { get; } =
    [
        new(
            "Search Knowledge",
            "Search my knowledge base for what I know about deployment architecture",
            AgentCapability.KnowledgeSearch),

        new(
            "Analyze Screen",
            "Explain the error on my current screen",
            AgentCapability.ScreenUnderstanding),

        new(
            "Create Report",
            "Create a weekly project report from my documents",
            AgentCapability.ReportGeneration),

        new(
            "Automate Workflow",
            "Search my documents about deployment architecture, summarize the result, and create a report from it",
            AgentCapability.KnowledgeSearch),
    ];
}
