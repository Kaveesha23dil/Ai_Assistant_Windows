namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// A capability the assistant must hold before performing an action. Every entry maps to a
/// consent switch the user controls, so a capability is never implicitly granted.
/// </summary>
public enum PermissionCapability
{
    /// <summary>Capture audio from the microphone for speech recognition.</summary>
    Microphone,

    /// <summary>Read or write text on the Windows clipboard.</summary>
    Clipboard,

    /// <summary>Search the local file system on the user's behalf.</summary>
    FileSearch,

    /// <summary>Capture the screen to an image file.</summary>
    ScreenCapture,

    /// <summary>Send captured screen content to an AI service for analysis.</summary>
    ScreenAnalysis,

    /// <summary>Change system-level state such as output volume or mute.</summary>
    SystemControl,

    /// <summary>Launch applications and open shell URIs.</summary>
    ApplicationLaunch,

    /// <summary>Run a web search in the default browser.</summary>
    WebSearch,

    /// <summary>Send a transcript or question to a cloud AI provider.</summary>
    CloudAI,

    /// <summary>Retain voice command history for later inspection.</summary>
    VoiceHistory
}
