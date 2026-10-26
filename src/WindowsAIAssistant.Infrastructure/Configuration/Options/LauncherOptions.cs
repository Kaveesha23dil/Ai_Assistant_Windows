using WindowsAIAssistant.Core.Models.Launcher;

namespace WindowsAIAssistant.Infrastructure.Configuration.Options;

/// <summary>
/// Configuration for the universal launcher.
/// </summary>
public sealed class LauncherOptions
{
    public const string SectionName = "Launcher";

    /// <summary>Gets or sets a value indicating whether the global shortcut is registered.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Gets or sets the global shortcut, such as <c>Alt+Space</c>.</summary>
    public string Hotkey { get; init; } = HotkeyGesture.Default.DisplayName;

    /// <summary>Gets or sets the greatest number of results returned for one search.</summary>
    public int MaximumResults { get; init; } = 20;

    /// <summary>Gets or sets how long typing must pause before a search starts.</summary>
    public int DebounceMilliseconds { get; init; } = 180;

    /// <summary>Gets or sets the time budget for one provider.</summary>
    public int ProviderTimeoutMilliseconds { get; init; } = 700;

    /// <summary>Gets or sets a value indicating whether recently chosen results may be kept.</summary>
    public bool RememberRecentItems { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the launcher should start with Windows. The
    /// setting is reserved until a startup registration is implemented.
    /// </summary>
    public bool LaunchAtStartup { get; init; }
}
