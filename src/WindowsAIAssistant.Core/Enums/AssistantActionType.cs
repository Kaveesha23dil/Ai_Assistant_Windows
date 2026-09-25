namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// Classifies the kind of action an assistant request represents.
/// </summary>
public enum AssistantActionType
{
    /// <summary>No action has been classified.</summary>
    None,

    /// <summary>A conversational AI exchange.</summary>
    Chat,

    /// <summary>Launch an application.</summary>
    OpenApplication,

    /// <summary>Search the local file system.</summary>
    SearchFile,

    /// <summary>Read the system clipboard.</summary>
    ReadClipboard,

    /// <summary>Retrieve Windows system information.</summary>
    SystemInformation,

    /// <summary>Create a task or reminder.</summary>
    CreateReminder,

    /// <summary>A multi-step automated workflow.</summary>
    Automation
}