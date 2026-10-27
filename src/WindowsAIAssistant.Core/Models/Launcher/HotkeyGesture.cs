namespace WindowsAIAssistant.Core.Models.Launcher;

/// <summary>
/// A keyboard shortcut that can be registered system-wide.
/// </summary>
public sealed record HotkeyGesture
{
    private static readonly HashSet<string> SupportedKeys = BuildSupportedKeys();

    private static readonly IReadOnlyDictionary<string, HotkeyModifiers> ModifierAliases =
        new Dictionary<string, HotkeyModifiers>(StringComparer.OrdinalIgnoreCase)
        {
            ["ALT"] = HotkeyModifiers.Alt,
            ["CTRL"] = HotkeyModifiers.Control,
            ["CONTROL"] = HotkeyModifiers.Control,
            ["SHIFT"] = HotkeyModifiers.Shift,
            ["WIN"] = HotkeyModifiers.Windows,
            ["WINDOWS"] = HotkeyModifiers.Windows,
        };

    /// <summary>Gets the default launcher shortcut.</summary>
    public static HotkeyGesture Default { get; } = new(HotkeyModifiers.Alt, "Space");

    public HotkeyGesture(HotkeyModifiers modifiers, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (modifiers == HotkeyModifiers.None)
        {
            throw new ArgumentException("A global shortcut needs at least one modifier.", nameof(modifiers));
        }

        if (!SupportedKeys.Contains(key.Trim()))
        {
            throw new ArgumentException($"'{key}' is not a supported shortcut key.", nameof(key));
        }

        Modifiers = modifiers;
        Key = CanonicalizeKey(key);
    }

    /// <summary>Gets the modifier keys that must be held.</summary>
    public HotkeyModifiers Modifiers { get; }

    /// <summary>Gets the non-modifier key, in its canonical spelling.</summary>
    public string Key { get; }

    /// <summary>Gets the shortcut as it is shown to a person.</summary>
    public string DisplayName => $"{FormatModifiers(Modifiers)}+{Key}";

    /// <summary>
    /// Parses a shortcut such as <c>Alt+Space</c> or <c>Ctrl+Shift+P</c>.
    /// </summary>
    public static bool TryParse(
        string? text,
        out HotkeyGesture? hotkey,
        out string? error)
    {
        hotkey = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Enter a shortcut such as Alt+Space.";
            return false;
        }

        var parts = text
            .Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
        {
            error = "Include at least one of Ctrl, Alt, Shift, or Win and one other key.";
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            if (!ModifierAliases.TryGetValue(parts[index], out var modifier))
            {
                error = $"'{parts[index]}' is not a shortcut modifier.";
                return false;
            }

            if (modifiers.HasFlag(modifier))
            {
                error = $"'{parts[index]}' is listed more than once.";
                return false;
            }

            modifiers |= modifier;
        }

        var key = parts[^1];
        if (!SupportedKeys.Contains(key))
        {
            error = $"'{key}' is not a supported shortcut key.";
            return false;
        }

        if (modifiers == HotkeyModifiers.None)
        {
            error = "Include at least one of Ctrl, Alt, Shift, or Win and one other key.";
            return false;
        }

        hotkey = new HotkeyGesture(modifiers, key);
        error = null;
        return true;
    }

    /// <summary>
    /// Parses a shortcut, or returns the default when the text cannot be used.
    /// </summary>
    public static HotkeyGesture ParseOrDefault(string? text)
    {
        return TryParse(text, out var hotkey, out _) && hotkey is not null
            ? hotkey
            : Default;
    }

    private static string FormatModifiers(HotkeyModifiers modifiers)
    {
        var parts = new List<string>(4);

        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            parts.Add("Win");
        }

        return string.Join('+', parts);
    }

    private static string CanonicalizeKey(string key) =>
        key.Trim().ToUpperInvariant() switch
        {
            "SPACE" => "Space",
            "ENTER" => "Enter",
            "ESCAPE" => "Escape",
            "TAB" => "Tab",
            "BACKSPACE" => "Backspace",
            "DELETE" => "Delete",
            "INSERT" => "Insert",
            "HOME" => "Home",
            "END" => "End",
            "PAGEUP" => "PageUp",
            "PAGEDOWN" => "PageDown",
            "LEFT" => "Left",
            "RIGHT" => "Right",
            "UP" => "Up",
            "DOWN" => "Down",
            _ => key.Trim().ToUpperInvariant(),
        };

    private static HashSet<string> BuildSupportedKeys()
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Space", "Enter", "Escape", "Tab", "Backspace", "Delete", "Insert",
            "Home", "End", "PageUp", "PageDown", "Left", "Right", "Up", "Down",
        };

        foreach (var character in "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")
        {
            keys.Add(character.ToString());
        }

        for (var functionKey = 1; functionKey <= 24; functionKey++)
        {
            keys.Add($"F{functionKey}");
        }

        return keys;
    }
}
