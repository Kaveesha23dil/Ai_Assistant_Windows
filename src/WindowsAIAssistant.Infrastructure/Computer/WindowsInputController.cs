using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Computer;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Computer;

namespace WindowsAIAssistant.Infrastructure.Computer;

public sealed class WindowsInputController : IInputController
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint KeyeventfUnicode = 0x0004;
    private const uint MouseeventfLeftdown = 0x0002;
    private const uint MouseeventfLeftup = 0x0004;
    private const uint MouseeventfRightdown = 0x0008;
    private const uint MouseeventfRightup = 0x0010;
    private const uint MouseeventfMiddledown = 0x0020;
    private const uint MouseeventfMiddleup = 0x0040;
    private const uint MouseeventfWheel = 0x0800;

    private readonly ILogger<WindowsInputController> _logger;

    public WindowsInputController(ILogger<WindowsInputController> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public async Task<Result> ClickAsync(MouseInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        if (input.IsCoordinateClick
            && !SetCursorPos(Math.Clamp(input.X!.Value, -32000, 32000), Math.Clamp(input.Y!.Value, -32000, 32000)))
        {
            return Result.Failure(ErrorCodes.ComputerActionFailed, "I couldn't move the mouse.");
        }

        var (down, up) = input.Button switch
        {
            MouseButtonType.Right => (MouseeventfRightdown, MouseeventfRightup),
            MouseButtonType.Middle => (MouseeventfMiddledown, MouseeventfMiddleup),
            _ => (MouseeventfLeftdown, MouseeventfLeftup)
        };

        if (input.DoubleClick)
        {
            for (var i = 0; i < 2; i++)
            {
                if (!MouseEvent(down) || !MouseEvent(up))
                {
                    return Result.Failure(ErrorCodes.ComputerActionFailed, "I couldn't click.");
                }

                await Task.Delay(80, cancellationToken).ConfigureAwait(false);
            }

            return Result.Success();
        }

        return MouseEvent(down) && MouseEvent(up)
            ? Result.Success()
            : Result.Failure(ErrorCodes.ComputerActionFailed, "I couldn't click.");
    }

    public Task<Result> MoveAsync(int x, int y, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return SetCursorPos(x, y)
            ? Task.FromResult(Result.Success())
            : Task.FromResult(Result.Failure(
                ErrorCodes.ComputerActionFailed,
                "I couldn't move the mouse."));
    }

    public Task<Result> ScrollAsync(int delta, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return MouseEvent(MouseeventfWheel, delta)
            ? Task.FromResult(Result.Success())
            : Task.FromResult(Result.Failure(
                ErrorCodes.ComputerActionFailed,
                "I couldn't scroll."));
    }

    public async Task<Result> TypeTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Result.Failure(ErrorCodes.ComputerActionFailed, "There was no text to type.");
        }

        foreach (var character in text)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (character == '\n')
            {
                if (!SendKey(VirtualKeyReturn, keyUp: false) || !SendKey(VirtualKeyReturn, keyUp: true))
                {
                    return Result.Failure(ErrorCodes.ComputerActionFailed, "I couldn't type the text.");
                }

                continue;
            }

            if (!SendUnicode(character, keyUp: false) || !SendUnicode(character, keyUp: true))
            {
                return Result.Failure(ErrorCodes.ComputerActionFailed, "I couldn't type the text.");
            }

            await Task.Delay(2, cancellationToken).ConfigureAwait(false);
        }

        return Result.Success();
    }

    public async Task<Result> PressKeyAsync(
        KeyboardInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        var keys = input.Keys.Count > 0
            ? input.Keys
            : input.Key is null ? [] : [input.Key];

        if (keys.Count == 0)
        {
            return Result.Failure(ErrorCodes.ComputerActionFailed, "No key was named.");
        }

        if (keys.Count > 1 || input.IsShortcut)
        {
            return await PressShortcutAsync(keys, cancellationToken).ConfigureAwait(false);
        }

        if (!TryParseKey(keys[0], out var key))
        {
            return Result.Failure(ErrorCodes.ComputerActionFailed, $"I don't know the key {keys[0]}.");
        }

        if (!SendKey(key, keyUp: false) || !SendKey(key, keyUp: true))
        {
            return Result.Failure(ErrorCodes.ComputerActionFailed, "I couldn't press that key.");
        }

        await Task.Delay(input.DelayMilliseconds, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> PressShortcutAsync(
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        cancellationToken.ThrowIfCancellationRequested();

        var parsed = new List<int>();
        foreach (var name in keys)
        {
            foreach (var part in name.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!TryParseKey(part, out var key))
                {
                    return Result.Failure(
                        ErrorCodes.ComputerActionFailed,
                        $"I don't know the key {part}.");
                }

                parsed.Add(key);
            }
        }

        if (parsed.Count == 0)
        {
            return Result.Failure(ErrorCodes.ComputerActionFailed, "No shortcut was named.");
        }

        foreach (var key in parsed)
        {
            if (!SendKey(key, keyUp: false))
            {
                ReleaseKeys(parsed);
                return Result.Failure(ErrorCodes.ComputerActionFailed, "I couldn't press that shortcut.");
            }
        }

        for (var index = parsed.Count - 1; index >= 0; index--)
        {
            if (!SendKey(parsed[index], keyUp: true))
            {
                ReleaseKeys(parsed);
                return Result.Failure(ErrorCodes.ComputerActionFailed, "I couldn't release that shortcut.");
            }
        }

        await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    private static void ReleaseKeys(IEnumerable<int> keys)
    {
        foreach (var key in keys)
        {
            _ = SendKey(key, keyUp: true);
        }
    }

    private static bool MouseEvent(uint flags, int data = 0)
    {
        var input = new Input
        {
            Type = InputMouse,
            Data = new InputUnion
            {
                Mouse = new NativeMouseInput
                {
                    Flags = flags,
                    MouseData = data
                }
            }
        };

        return SendInput(1, [input], Marshal.SizeOf<Input>()) == 1;
    }

    private static bool SendKey(int key, bool keyUp) => SendInput(
        1,
        [
            new Input
            {
                Type = InputKeyboard,
                Data = new InputUnion
                {
                    Keyboard = new NativeKeyboardInput
                    {
                        VirtualKey = (ushort)key,
                        Flags = keyUp ? KeyeventfKeyup : 0
                    }
                }
            }
        ],
        Marshal.SizeOf<Input>()) == 1;

    private static bool SendUnicode(char character, bool keyUp) => SendInput(
        1,
        [
            new Input
            {
                Type = InputKeyboard,
                Data = new InputUnion
                {
                    Keyboard = new NativeKeyboardInput
                    {
                        ScanCode = character,
                        Flags = KeyeventfUnicode | (keyUp ? KeyeventfKeyup : 0)
                    }
                }
            }
        ],
        Marshal.SizeOf<Input>()) == 1;

    private static bool TryParseKey(string name, out int key)
    {
        key = 0;
        var cleaned = name.Trim();

        if (string.IsNullOrEmpty(cleaned))
        {
            return false;
        }

        if (knownKeys.TryGetValue(cleaned, out key))
        {
            return true;
        }

        if (cleaned.Length == 1)
        {
            var character = char.ToUpperInvariant(cleaned[0]);
            if (character is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                key = character;
                return true;
            }
        }

        return false;
    }

    private static readonly Dictionary<string, int> knownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CTRL"] = 0x11,
        ["CONTROL"] = 0x11,
        ["SHIFT"] = 0x10,
        ["ALT"] = 0x12,
        ["WIN"] = 0x5B,
        ["WINDOWS"] = 0x5B,
        ["ENTER"] = VirtualKeyReturn,
        ["RETURN"] = VirtualKeyReturn,
        ["TAB"] = 0x09,
        ["ESC"] = 0x1B,
        ["ESCAPE"] = 0x1B,
        ["SPACE"] = 0x20,
        ["BACKSPACE"] = 0x08,
        ["DELETE"] = 0x2E,
        ["DEL"] = 0x2E,
        ["INSERT"] = 0x2D,
        ["HOME"] = 0x24,
        ["END"] = 0x23,
        ["PAGEUP"] = 0x21,
        ["PGUP"] = 0x21,
        ["PAGEDOWN"] = 0x22,
        ["PGDN"] = 0x22,
        ["UP"] = 0x26,
        ["DOWN"] = 0x28,
        ["LEFT"] = 0x25,
        ["RIGHT"] = 0x27,
        ["F1"] = 0x70,
        ["F2"] = 0x71,
        ["F3"] = 0x72,
        ["F4"] = 0x73,
        ["F5"] = 0x74,
        ["F6"] = 0x75,
        ["F7"] = 0x76,
        ["F8"] = 0x77,
        ["F9"] = 0x78,
        ["F10"] = 0x79,
        ["F11"] = 0x7A,
        ["F12"] = 0x7B
    };

    private const int VirtualKeyReturn = 0x0D;

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public NativeMouseInput Mouse;

        [FieldOffset(0)]
        public NativeKeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouseInput
    {
        public int X;
        public int Y;
        public int MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern void mouse_event(
        uint flags,
        uint dx,
        uint dy,
        int data,
        UIntPtr extraInfo);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
}

