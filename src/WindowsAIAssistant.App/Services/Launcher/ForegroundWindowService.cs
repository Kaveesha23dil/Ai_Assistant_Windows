using System.Runtime.InteropServices;

namespace WindowsAIAssistant.App.Services.Launcher;

/// <summary>
/// Remembers the window that had focus before the launcher appeared and gives it back when the
/// launcher closes. Without this, dismissing the launcher leaves focus nowhere useful and the
/// person has to click back into whatever they were doing.
/// </summary>
public sealed class ForegroundWindowService
{
    private nint _previous;

    /// <summary>Remembers the current foreground window.</summary>
    public void Capture()
    {
        var current = GetForegroundWindow();
        if (current != 0)
        {
            _previous = current;
        }
    }

    /// <summary>
    /// Restores the remembered window when the launcher is still the foreground window. If
    /// another window has already taken focus, it is left alone.
    /// </summary>
    public void Restore(nint launcherWindow)
    {
        var previous = _previous;
        if (previous == 0)
        {
            return;
        }

        var current = GetForegroundWindow();
        if (current != 0 && current != launcherWindow)
        {
            return;
        }

        ShowWindow(previous, SwRestore);
        SetForegroundWindow(previous);
        _previous = 0;
    }

    /// <summary>Brings a window to the foreground, restoring it first if it was minimized.</summary>
    public static void Activate(nint window)
    {
        if (window == 0)
        {
            return;
        }

        if (IsIconic(window))
        {
            ShowWindow(window, SwRestore);
        }

        SetForegroundWindow(window);
    }

    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);
}
