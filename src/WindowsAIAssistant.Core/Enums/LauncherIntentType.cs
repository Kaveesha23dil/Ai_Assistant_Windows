namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// The kind of request a launcher query is asking for.
/// </summary>
public enum LauncherIntentType
{
    /// <summary>Search everything that can be searched locally.</summary>
    Universal,

    /// <summary>Find or launch an installed application.</summary>
    Application,

    /// <summary>Find a file by name or path.</summary>
    File,

    /// <summary>Search the local knowledge base.</summary>
    Knowledge,

    /// <summary>Run an application command.</summary>
    Command,

    /// <summary>Open a setting or settings page.</summary>
    Setting,

    /// <summary>Evaluate an arithmetic expression.</summary>
    Calculator,

    /// <summary>Hand the query to the conversational assistant.</summary>
    AiQuestion,

    /// <summary>Search the web.</summary>
    WebSearch,
}
