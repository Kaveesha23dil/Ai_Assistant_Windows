using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Computer;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Computer;

namespace WindowsAIAssistant.Infrastructure.Computer;

public sealed class WindowsWindowManager : IWindowManager
{
    private const int SwHide = 0;
    private const int SwRestore = 9;
    private const int SwMinimize = 6;
    private const int SwMaximize = 3;
    private const int GwlExstyle = -20;
    private const int WsExToolwindow = 0x00000080;
    private const uint WmClose = 0x0010;
    private const uint MonitorinfofPrimary = 0x00000001;

    private readonly ILogger<WindowsWindowManager> _logger;

    public WindowsWindowManager(ILogger<WindowsWindowManager> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public Task<Result<IReadOnlyList<WindowInfo>>> GetWindowsAsync(
        bool includeHidden,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var foreground = GetForegroundWindow();
            var found = new List<WindowInfo>();

            EnumWindows((handle, _) =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!includeHidden && !IsWindowVisible(handle))
                {
                    return true;
                }

                var title = ReadTitle(handle);
                var processId = ReadProcessId(handle);
                var processName = ReadProcessName(processId);

                if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(processName))
                {
                    return true;
                }

                found.Add(new WindowInfo
                {
                    Handle = handle.ToInt64(),
                    Title = title,
                    ProcessId = processId,
                    ProcessName = processName,
                    IsVisible = IsWindowVisible(handle),
                    IsForeground = handle == foreground,
                    Bounds = ReadBounds(handle),
                    MonitorIndex = ReadMonitorIndex(handle)
                });

                return true;
            }, IntPtr.Zero);

            return Task.FromResult(Result<IReadOnlyList<WindowInfo>>.Success(found));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Enumerating Windows windows failed.");
            return Task.FromResult(Result<IReadOnlyList<WindowInfo>>.Failure(
                ErrorCodes.ComputerActionFailed,
                "I couldn't read the list of open windows."));
        }
    }

    public Task<Result<WindowInfo>> GetActiveWindowAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var handle = GetForegroundWindow();
            if (handle == IntPtr.Zero)
            {
                return Task.FromResult(Result<WindowInfo>.Failure(
                    ErrorCodes.ComputerTargetNotFound,
                    "There is no active window."));
            }

            var window = ReadWindow(handle);
            return window is null
                ? Task.FromResult(Result<WindowInfo>.Failure(
                    ErrorCodes.ComputerTargetNotFound,
                    "I couldn't identify the active window."))
                : Task.FromResult(Result<WindowInfo>.Success(window));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Reading the active window failed.");
            return Task.FromResult(Result<WindowInfo>.Failure(
                ErrorCodes.ComputerActionFailed,
                "I couldn't read the active window."));
        }
    }

    public async Task<Result<WindowInfo>> FindWindowAsync(
        string titleOrProcess,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(titleOrProcess);

        var windows = await GetWindowsAsync(includeHidden: false, cancellationToken).ConfigureAwait(false);
        if (windows.IsFailure || windows.Value is null)
        {
            return Result<WindowInfo>.Failure(
                windows.ErrorCode ?? ErrorCodes.ComputerActionFailed,
                windows.ErrorMessage ?? "I couldn't read the open windows.");
        }

        var needle = titleOrProcess.Trim();
        var matches = windows.Value
            .Where(window =>
                window.Title.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || (window.ProcessName?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderByDescending(window => window.IsForeground)
            .ThenByDescending(window => window.IsVisible)
            .ToArray();

        if (matches.Length > 0)
        {
            return Result<WindowInfo>.Success(matches[0]);
        }

        return Result<WindowInfo>.Failure(
            ErrorCodes.ComputerTargetNotFound,
            $"I couldn't find a window called {needle}.");
    }

    public Task<Result> SwitchToAsync(WindowInfo window, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var handle = new IntPtr(window.Handle);
            if (!IsWindow(handle))
            {
                return Task.FromResult(Result.Failure(
                    ErrorCodes.ComputerTargetNotFound,
                    "That window is no longer open."));
            }

            if (IsIconic(handle))
            {
                ShowWindow(handle, SwRestore);
            }

            if (!SetForegroundWindow(handle))
            {
                BringWindowToTop(handle);
                SetForegroundWindow(handle);
            }

            return Task.FromResult(GetForegroundWindow() == handle
                ? Result.Success()
                : Result.Failure(
                    ErrorCodes.ComputerWrongActiveWindow,
                    "Windows did not bring that window to the front."));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Switching windows failed.");
            return Task.FromResult(Result.Failure(
                ErrorCodes.ComputerActionFailed,
                "I couldn't switch to that window."));
        }
    }

    public Task<Result> MinimizeAsync(WindowInfo window, CancellationToken cancellationToken = default) =>
        ShowAsync(window, SwMinimize, "minimize", cancellationToken);

    public Task<Result> MaximizeAsync(WindowInfo window, CancellationToken cancellationToken = default) =>
        ShowAsync(window, SwMaximize, "maximize", cancellationToken);

    public Task<Result> RestoreAsync(WindowInfo window, CancellationToken cancellationToken = default) =>
        ShowAsync(window, SwRestore, "restore", cancellationToken);

    public Task<Result> CloseAsync(WindowInfo window, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var handle = new IntPtr(window.Handle);
            if (!IsWindow(handle))
            {
                return Task.FromResult(Result.Success());
            }

            PostMessage(handle, WmClose, IntPtr.Zero, IntPtr.Zero);
            return Task.FromResult(Result.Success());
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Closing a window failed.");
            return Task.FromResult(Result.Failure(
                ErrorCodes.ComputerActionFailed,
                "I couldn't close that window."));
        }
    }

    public Task<Result<IReadOnlyList<MonitorInfo>>> GetMonitorsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var monitors = new List<MonitorInfo>();
            var index = 0;

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
                if (!GetMonitorInfo(monitor, ref info))
                {
                    return true;
                }

                monitors.Add(new MonitorInfo
                {
                    Index = index++,
                    Name = string.IsNullOrWhiteSpace(info.DeviceName) ? "Display" : info.DeviceName,
                    Bounds = ToRect(info.Monitor),
                    IsPrimary = (info.Flags & MonitorinfofPrimary) != 0,
                    DpiScale = ReadDpiScale(monitor)
                });

                return true;
            }, IntPtr.Zero);

            return Task.FromResult(Result<IReadOnlyList<MonitorInfo>>.Success(monitors));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Enumerating monitors failed.");
            return Task.FromResult(Result<IReadOnlyList<MonitorInfo>>.Failure(
                ErrorCodes.ComputerActionFailed,
                "I couldn't read the display layout."));
        }
    }

    public Task<Result<ComputerRect>> GetCursorPositionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return GetCursorPos(out var point)
            ? Task.FromResult(Result<ComputerRect>.Success(new ComputerRect(point.X, point.Y, 0, 0)))
            : Task.FromResult(Result<ComputerRect>.Failure(
                ErrorCodes.ComputerActionFailed,
                "I couldn't read the mouse position."));
    }

    private Task<Result> ShowAsync(
        WindowInfo window,
        int command,
        string verb,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var handle = new IntPtr(window.Handle);
            if (!IsWindow(handle))
            {
                return Task.FromResult(Result.Failure(
                    ErrorCodes.ComputerTargetNotFound,
                    "That window is no longer open."));
            }

            ShowWindow(handle, command);
            return Task.FromResult(Result.Success());
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Trying to {Verb} a window failed.", verb);
            return Task.FromResult(Result.Failure(
                ErrorCodes.ComputerActionFailed,
                $"I couldn't {verb} that window."));
        }
    }

    private static WindowInfo? ReadWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !IsWindow(handle))
        {
            return null;
        }

        var processId = ReadProcessId(handle);
        return new WindowInfo
        {
            Handle = handle.ToInt64(),
            Title = ReadTitle(handle),
            ProcessId = processId,
            ProcessName = ReadProcessName(processId),
            IsVisible = IsWindowVisible(handle),
            IsForeground = GetForegroundWindow() == handle,
            Bounds = ReadBounds(handle),
            MonitorIndex = ReadMonitorIndex(handle)
        };
    }

    private static string ReadTitle(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new StringBuilder(length + 1);
        _ = GetWindowText(handle, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static int ReadProcessId(IntPtr handle)
    {
        _ = GetWindowThreadProcessId(handle, out var processId);
        return unchecked((int)processId);
    }

    private static string? ReadProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static ComputerRect ReadBounds(IntPtr handle) =>
        GetWindowRect(handle, out var rect) ? ToRect(rect) : ComputerRect.Empty;

    private static int ReadMonitorIndex(IntPtr handle)
    {
        var monitor = MonitorFromWindow(handle, 2);
        var index = 0;

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (current, _, _, _) =>
        {
            if (current == monitor)
            {
                return false;
            }

            index++;
            return true;
        }, IntPtr.Zero);

        return index;
    }

    private static double ReadDpiScale(IntPtr monitor)
    {
        try
        {
            return GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 && dpiX > 0
                ? dpiX / 96.0
                : 1.0;
        }
        catch (Exception)
        {
            return 1.0;
        }
    }

    private static ComputerRect ToRect(NativeRect rect) =>
        new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

    private delegate bool MonitorEnumProc(
        IntPtr monitor,
        IntPtr deviceContext,
        IntPtr rect,
        IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfoEx
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr deviceContext,
        IntPtr clip,
        MonitorEnumProc callback,
        IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr handle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int maximum);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}
