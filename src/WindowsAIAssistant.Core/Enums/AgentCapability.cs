namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// A capability the assistant can advertise, independent of the tool that implements it.
/// <para>
/// Capabilities are what a person is shown and what a plan is checked against, and they are
/// deliberately coarser than tools. "Screen understanding" is one capability whether it was
/// reached by capturing a display, a window, or a region, and the workspace shows the capability
/// rather than the three tools behind it — a list of tool names is a description of the
/// implementation, not of what the assistant can do.
/// </para>
/// </summary>
public enum AgentCapability
{
    /// <summary>Read a document and summarize or question it.</summary>
    DocumentAnalysis = 0,

    /// <summary>Search the person's own indexed documents and answer from them.</summary>
    KnowledgeSearch = 1,

    /// <summary>Look at the screen and explain what is there.</summary>
    ScreenUnderstanding = 2,

    /// <summary>Accept a spoken request and answer aloud.</summary>
    VoiceControl = 3,

    /// <summary>Write a report to a file in a chosen format.</summary>
    ReportGeneration = 4,

    /// <summary>Find files by name on this machine.</summary>
    FileSearch = 5,

    /// <summary>Report what this computer is and how it is doing.</summary>
    SystemInformation = 6,

    /// <summary>Evaluate arithmetic without involving a model.</summary>
    Calculation = 7
}
