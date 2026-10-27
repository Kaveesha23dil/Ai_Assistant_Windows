namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// The category a universal launcher result belongs to.
/// </summary>
public enum SearchResultType
{
    /// <summary>An installed application.</summary>
    Application,

    /// <summary>A file or folder.</summary>
    File,

    /// <summary>A passage from the local knowledge base.</summary>
    Knowledge,

    /// <summary>An application command.</summary>
    Command,

    /// <summary>An application or Windows setting.</summary>
    Setting,

    /// <summary>A calculated value.</summary>
    Calculator,

    /// <summary>A request to hand to the assistant.</summary>
    Ai,

    /// <summary>A web search.</summary>
    Web,

    /// <summary>A non-actionable status result.</summary>
    Message,
}
