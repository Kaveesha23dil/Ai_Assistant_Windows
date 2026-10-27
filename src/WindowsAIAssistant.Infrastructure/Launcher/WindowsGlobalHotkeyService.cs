using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Launcher;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Launcher;

namespace WindowsAIAssistant.Infrastructure.Launcher;

/// <summary>
/// Registers a system-wide shortcut through <c>RegisterHotKey</c>.
/// <para>
/// Registration and the message loop live on a dedicated thread with a message-only window, so
/// the service needs no window from the presentation layer and does not poll the keyboard. The
/// <see cref="Pressed"/> event is raised on that thread; a host is responsible for marshalling
/// it onto its own dispatcher.
/// </para>
/// </summary>
public sealed class WindowsGlobalHotkeyService : IGlobalHotkeyService
{
    private const int HotkeyId = 0x4C41;
    private const int WmHotkey = 0x0312;
    private const int WmClose = 0x0010;
    private const int WmDestroy = 0x0002;
    private const int ModNoRepeat = 0x4000;
    private const int ErrorHotkeyAlreadyRegistered = 1409;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly ILogger<WindowsGlobalHotkeyService> _logger;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);

    private Thread? _thread;
    private uint _threadId;
    private IntPtr _window;
    private HotkeyGesture? _currentHotkey;

    public WindowsGlobalHotkeyService(ILogger<WindowsGlobalHotkeyService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsRegistered => CurrentHotkey is not null;

    /// <inheritdoc />
    public HotkeyGesture? CurrentHotkey
    {
        get
        {
            lock (_gate)
            {
                return _currentHotkey;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler? Pressed;

    /// <inheritdoc />
    public async Task<Result> RegisterAsync(
        HotkeyGesture hotkey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hotkey);

        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopThreadAsync().ConfigureAwait(false);

            var completion = new TaskCompletionSource<Result>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var thread = new Thread(() => RunMessageLoop(hotkey, completion))
            {
                IsBackground = true,
                Name = "Assistant launcher hotkey",
            };
            thread.SetApartmentState(ApartmentState.STA);

            lock (_gate)
            {
                _thread = thread;
            }

            thread.Start();

            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <inheritdoc />
    public async Task UnregisterAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopThreadAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await UnregisterAsync().ConfigureAwait(false);
        _lifecycle.Dispose();
    }

    private void RunMessageLoop(
        HotkeyGesture requested,
        TaskCompletionSource<Result> completion)
    {
        var className = $"WindowsAIAssistant.Hotkey.{Guid.NewGuid():N}";
        var instance = GetModuleHandle(null);
        var windowProcedure = new WindowProcedure(HandleWindowMessage);

        try
        {
            lock (_gate)
            {
                _threadId = GetCurrentThreadId();
            }

            var windowClass = new WindowClass
            {
                Size = (uint)Marshal.SizeOf<WindowClass>(),
                Style = 0,
                WindowProcedure = windowProcedure,
                ClassName = className,
                Instance = instance,
            };

            if (RegisterClassEx(ref windowClass) == 0)
            {
                completion.TrySetResult(FailureFor(Marshal.GetLastWin32Error()));
                return;
            }

            var window = CreateWindowEx(
                0,
                className,
                "Windows AI Assistant Hotkey",
                0,
                0,
                0,
                0,
                0,
                HwndMessage,
                IntPtr.Zero,
                instance,
                IntPtr.Zero);

            if (window == IntPtr.Zero)
            {
                _logger.LogWarning("The launcher hotkey window could not be created.");
                completion.TrySetResult(FailureFor(Marshal.GetLastWin32Error()));
                return;
            }

            lock (_gate)
            {
                _window = window;
            }

            var virtualKey = ToVirtualKey(requested.Key);
            var modifiers = ToNativeModifiers(requested.Modifiers) | ModNoRepeat;

            if (!RegisterHotKey(window, HotkeyId, modifiers, virtualKey))
            {
                var error = Marshal.GetLastWin32Error();
                DestroyWindow(window);
                completion.TrySetResult(FailureFor(error));
                return;
            }

            lock (_gate)
            {
                _currentHotkey = requested;
            }

            completion.TrySetResult(Result.Success());

            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The launcher hotkey listener failed.");
            completion.TrySetResult(Result.Failure(
                ErrorCodes.LauncherHotkeyUnavailable,
                "The shortcut could not be registered. Choose another shortcut."));
        }
        finally
        {
            lock (_gate)
            {
                if (_window != IntPtr.Zero)
                {
                    UnregisterHotKey(_window, HotkeyId);
                }

                _window = IntPtr.Zero;
                _threadId = 0;
                _currentHotkey = null;
                _thread = null;
            }

            GC.KeepAlive(windowProcedure);
        }
    }

    private IntPtr HandleWindowMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            try
            {
                Pressed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "A launcher shortcut handler failed.");
            }

            return IntPtr.Zero;
        }

        if (message == WmClose)
        {
            DestroyWindow(window);
            return IntPtr.Zero;
        }

        if (message == WmDestroy)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }

        return DefWindowProc(window, message, wParam, lParam);
    }

    private async Task StopThreadAsync()
    {
        Thread? thread;
        IntPtr window;
        uint threadId;

        lock (_gate)
        {
            thread = _thread;
            window = _window;
            threadId = _threadId;
        }

        if (thread is null)
        {
            return;
        }

        if (window != IntPtr.Zero)
        {
            PostMessage(window, WmClose, IntPtr.Zero, IntPtr.Zero);
        }
        else if (threadId != 0)
        {
            PostThreadMessage(threadId, 0x0012, IntPtr.Zero, IntPtr.Zero);
        }

        await Task.Run(() =>
        {
            if (!thread.Join(TimeSpan.FromSeconds(3)))
            {
                _logger.LogWarning("The launcher hotkey listener did not stop within three seconds.");
            }
        }).ConfigureAwait(false);

        lock (_gate)
        {
            if (ReferenceEquals(_thread, thread))
            {
                _thread = null;
                _currentHotkey = null;
            }
        }
    }

    private Result FailureFor(int nativeError)
    {
        if (nativeError == ErrorHotkeyAlreadyRegistered)
        {
            _logger.LogInformation("The requested launcher shortcut is already in use.");
            return Result.Failure(
                ErrorCodes.LauncherHotkeyUnavailable,
                "This shortcut is already being used. Choose another shortcut.");
        }

        _logger.LogWarning(
            "Registering the launcher shortcut failed with Win32 error {Error}.",
            nativeError);

        return Result.Failure(
            ErrorCodes.LauncherHotkeyUnavailable,
            "This shortcut could not be registered. Choose another shortcut.");
    }

    private static uint ToVirtualKey(string key) =>
        key.ToUpperInvariant() switch
        {
            "SPACE" => 0x20,
            "ENTER" => 0x0D,
            "ESCAPE" => 0x1B,
            "TAB" => 0x09,
            "BACKSPACE" => 0x08,
            "DELETE" => 0x2E,
            "INSERT" => 0x2D,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" => 0x21,
            "PAGEDOWN" => 0x22,
            "LEFT" => 0x25,
            "RIGHT" => 0x27,
            "UP" => 0x26,
            "DOWN" => 0x28,
            _ when key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]) =>
                char.ToUpperInvariant(key[0]),
            _ when key.Length is 2 or 3 && key[0] == 'F'
                && int.TryParse(key.AsSpan(1), out var functionKey)
                && functionKey is >= 1 and <= 24 => (uint)(0x70 + functionKey - 1),
            _ => 0,
        };

    private static uint ToNativeModifiers(HotkeyModifiers modifiers)
    {
        var native = 0u;

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            native |= 0x0001;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            native |= 0x0002;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            native |= 0x0004;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            native |= 0x0008;
        }

        return native;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public WindowProcedure? WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string? ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Window;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PointX;
        public int PointY;
        public uint Private;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("user32.dll")]
    private static extern int GetMessage(
        out NativeMessage message,
        IntPtr window,
        uint minimumMessage,
        uint maximumMessage);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref NativeMessage message);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(
        uint threadId,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);
}
