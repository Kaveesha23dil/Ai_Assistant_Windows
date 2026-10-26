namespace WindowsAIAssistant.Core.Models.Launcher;

/// <summary>
/// Reports that the launcher should be shown or hidden.
/// </summary>
public sealed class LauncherVisibilityChangedEventArgs : EventArgs
{
    public LauncherVisibilityChangedEventArgs(bool isVisible, string reason)
    {
        IsVisible = isVisible;
        Reason = reason;
    }

    /// <summary>Gets a value indicating whether the launcher should be visible.</summary>
    public bool IsVisible { get; }

    /// <summary>Gets what asked for the change, such as <c>Hotkey</c> or <c>Request</c>.</summary>
    public string Reason { get; }
}
