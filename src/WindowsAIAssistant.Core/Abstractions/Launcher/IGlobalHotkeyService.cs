using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Launcher;

namespace WindowsAIAssistant.Core.Abstractions.Launcher;

/// <summary>
/// Registers one keyboard shortcut with the operating system and reports when it is pressed.
/// </summary>
public interface IGlobalHotkeyService : IAsyncDisposable
{
    /// <summary>Gets a value indicating whether a shortcut is currently registered.</summary>
    bool IsRegistered { get; }

    /// <summary>Gets the shortcut currently registered, or <see langword="null"/>.</summary>
    HotkeyGesture? CurrentHotkey { get; }

    /// <summary>Raised when the registered shortcut is pressed.</summary>
    event EventHandler? Pressed;

    /// <summary>
    /// Registers a shortcut, replacing any previous registration.
    /// </summary>
    Task<Result> RegisterAsync(
        HotkeyGesture hotkey,
        CancellationToken cancellationToken = default);

    /// <summary>Removes the current registration.</summary>
    Task UnregisterAsync(CancellationToken cancellationToken = default);
}
