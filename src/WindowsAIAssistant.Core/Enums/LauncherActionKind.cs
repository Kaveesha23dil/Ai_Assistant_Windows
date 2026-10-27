namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// The action the launcher performs when a result is chosen.
/// </summary>
public enum LauncherActionKind
{
    /// <summary>No action is attached to the result.</summary>
    None,

    /// <summary>Launch an installed application by name.</summary>
    LaunchApplication,

    /// <summary>Open a file or folder with the shell.</summary>
    OpenFile,

    /// <summary>Open the knowledge page for a query.</summary>
    OpenKnowledge,

    /// <summary>Navigate to an application route.</summary>
    Navigate,

    /// <summary>Open an absolute URI with the shell.</summary>
    OpenUri,

    /// <summary>Copy text to the clipboard.</summary>
    CopyText,

    /// <summary>Hand a question to the conversational assistant.</summary>
    AskAssistant,
}
