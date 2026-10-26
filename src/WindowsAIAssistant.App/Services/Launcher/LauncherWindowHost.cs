using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using WindowsAIAssistant.App.Views.Launcher;
using WindowsAIAssistant.Core.Abstractions.Launcher;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Launcher;
using Windows.Graphics;
using WinRT.Interop;

namespace WindowsAIAssistant.App.Services.Launcher;

/// <summary>
/// Connects the headless launcher state to its window. It owns showing, positioning, hiding, and
/// the closing rule; the window itself only handles its content and focus.
/// </summary>
public sealed class LauncherWindowHost : IDisposable
{
    private const int DesignWidth = 760;
    private const int DesignHeight = 480;

    private readonly LauncherWindow _window;
    private readonly IAssistantLauncherService _launcher;
    private readonly ForegroundWindowService _foreground;
    private readonly ILogger<LauncherWindowHost> _logger;

    private bool _started;
    private bool _closing;
    private bool _disposed;

    public LauncherWindowHost(
        LauncherWindow window,
        IAssistantLauncherService launcher,
        ForegroundWindowService foreground,
        ILogger<LauncherWindowHost> logger)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(foreground);
        ArgumentNullException.ThrowIfNull(logger);

        _window = window;
        _launcher = launcher;
        _foreground = foreground;
        _logger = logger;
    }

    /// <summary>
    /// Starts listening to the launcher state and registers the shortcut. A registration failure
    /// is returned so the shell can explain which key is unavailable.
    /// </summary>
    public async Task<Result> StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            return Result.Success();
        }

        _started = true;
        _launcher.VisibilityChanged += OnVisibilityChanged;

        if (GetAppWindow() is { } appWindow)
        {
            appWindow.Closing += OnWindowClosing;
        }

        var result = await _launcher.StartAsync(cancellationToken).ConfigureAwait(true);
        if (result.IsFailure)
        {
            _logger.LogWarning("The launcher shortcut is unavailable.");
        }

        return result;
    }

    /// <summary>Stops the shortcut and closes the hidden window as part of application shutdown.</summary>
    public async Task StopAsync()
    {
        if (_disposed)
        {
            return;
        }

        _closing = true;
        _launcher.VisibilityChanged -= OnVisibilityChanged;

        if (GetAppWindow() is { } appWindow)
        {
            appWindow.Closing -= OnWindowClosing;
        }

        try
        {
            await _launcher.StopAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Stopping the launcher failed.");
        }

        _window.AllowClose();
        _window.Close();
    }

    private void OnVisibilityChanged(object? sender, LauncherVisibilityChangedEventArgs args)
    {
        _window.DispatcherQueue.TryEnqueue(() =>
        {
            if (args.IsVisible)
            {
                Show();
            }
            else
            {
                Hide();
            }
        });
    }

    private void Show()
    {
        if (_closing || _disposed)
        {
            return;
        }

        try
        {
            _foreground.Capture();
            Position();
            _window.PrepareForShow();
            _window.Activate();
            ForegroundWindowService.Activate(_window.Handle);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The launcher window could not be shown.");
        }
    }

    private void Hide()
    {
        if (_closing || _disposed)
        {
            return;
        }

        var handle = _window.Handle;

        try
        {
            GetAppWindow()?.Hide();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "The launcher window could not be hidden.");
        }

        _foreground.Restore(handle);
    }

    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closing)
        {
            return;
        }

        args.Cancel = true;
        _ = _launcher.HideAsync();
    }

    private void Position()
    {
        var appWindow = GetAppWindow();
        if (appWindow is null)
        {
            return;
        }

        var work = GetWorkArea();
        var scale = GetDpiForWindow(_window.Handle) / 96.0;
        scale = scale <= 0 ? 1 : scale;

        var width = (int)Math.Min(DesignWidth * scale, work.Width * 0.9);
        var height = (int)Math.Min(DesignHeight * scale, work.Height * 0.9);
        var x = work.X + ((work.Width - width) / 2);
        var y = work.Y + ((work.Height - height) / 3);

        appWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private static RectInt32 GetWorkArea()
    {
        try
        {
            var foreground = GetForegroundWindow();
            var monitor = MonitorFromWindow(
                foreground == 0 ? IntPtr.Zero : foreground,
                MonitorDefaultToNearest);

            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                return new RectInt32(
                    info.Work.Left,
                    info.Work.Top,
                    info.Work.Right - info.Work.Left,
                    info.Work.Bottom - info.Work.Top);
            }
        }
        catch
        {
            // The primary display is the fallback below.
        }

        return new RectInt32(0, 0, 1280, 800);
    }

    private AppWindow? GetAppWindow()
    {
        try
        {
            var handle = _window.Handle;
            return handle == 0
                ? null
                : AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(handle));
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _launcher.VisibilityChanged -= OnVisibilityChanged;
    }

    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }
}
