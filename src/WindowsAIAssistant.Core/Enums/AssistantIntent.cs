namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// Classifies the intent extracted from a spoken or typed assistant request.
/// <para>
/// The assistant never executes an action directly from raw text. A transcript is first
/// converted into one of these intents, which a validated handler then executes. Unknown
/// values are intentionally reserved for future capabilities so the enum stays extensible
/// without breaking persisted history entries.
/// </para>
/// </summary>
public enum AssistantIntent
{
    /// <summary>The transcript could not be mapped to a known capability.</summary>
    Unknown,

    /// <summary>A free-form question routed to the configured AI service.</summary>
    AIQuestion,

    /// <summary>Launch a resolved, allow-listed application.</summary>
    OpenApplication,

    /// <summary>Close a running application. Requires confirmation.</summary>
    CloseApplication,

    /// <summary>Open a well-known user folder such as Downloads or Documents.</summary>
    OpenFolder,

    /// <summary>Open a Windows Settings page through a supported settings URI.</summary>
    OpenSettings,

    /// <summary>Run a web search through the default search provider.</summary>
    WebSearch,

    /// <summary>Run a web search against a specific site such as YouTube.</summary>
    YouTubeSearch,

    /// <summary>Search the local file system through the existing file search service.</summary>
    FileSearch,

    /// <summary>Read the text currently held on the clipboard.</summary>
    ReadClipboard,

    /// <summary>Summarize or explain the clipboard contents through the AI service.</summary>
    SummarizeClipboard,

    /// <summary>Report a summary of the host system.</summary>
    GetSystemInformation,

    /// <summary>Report battery charge and charging state.</summary>
    GetBatteryStatus,

    /// <summary>Report memory usage.</summary>
    GetMemoryUsage,

    /// <summary>Report free disk space.</summary>
    GetStorageUsage,

    /// <summary>Increase the system output volume.</summary>
    IncreaseVolume,

    /// <summary>Decrease the system output volume.</summary>
    DecreaseVolume,

    /// <summary>Set the system output volume to an explicit percentage.</summary>
    SetVolume,

    /// <summary>Mute the system output.</summary>
    MuteVolume,

    /// <summary>Unmute the system output.</summary>
    UnmuteVolume,

    /// <summary>Capture the primary display to an image file.</summary>
    TakeScreenshot,

    /// <summary>Report the current local time without involving the AI service.</summary>
    GetTime,

    /// <summary>Report the current local date without involving the AI service.</summary>
    GetDate,

    /// <summary>Begin capturing speech from the microphone.</summary>
    StartListening,

    /// <summary>Stop capturing speech and keep the partial transcript.</summary>
    StopListening,

    /// <summary>Abort the active voice operation and discard the transcript.</summary>
    Cancel,

    /// <summary>Speak supplied text aloud.</summary>
    SpeakText,

    /// <summary>Stop speech synthesis immediately.</summary>
    StopSpeaking,

    /// <summary>Repeat the most recent assistant response.</summary>
    RepeatResponse,

    /// <summary>Confirm a command that was previously held back for confirmation.</summary>
    ConfirmCommand
}
