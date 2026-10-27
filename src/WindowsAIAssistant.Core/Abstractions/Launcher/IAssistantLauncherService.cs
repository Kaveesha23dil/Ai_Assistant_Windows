using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Launcher;

namespace WindowsAIAssistant.Core.Abstractions.Launcher;

/// <summary>
/// Owns the launcher's state and the shortcut that makes it appear.
/// <para>
/// It does not create or touch a window. A host subscribes to
/// <see cref="VisibilityChanged"/> and is responsible for presenting the launcher.
/// </para>
/// </summary>
public interface IAssistantLauncherService
{
    /// <summary>Gets a value indicating whether the launcher is logically visible.</summary>
    bool IsVisible { get; }

    /// <summary>Gets the shortcut currently registered, when there is one.</summary>
    string? ActiveHotkey { get; }

    /// <summary>Gets the most recent shortcut registration failure, when there was one.</summary>
    Result? HotkeyRegistrationError { get; }

    /// <summary>Raised when the launcher should be shown or hidden.</summary>
    event EventHandler<LauncherVisibilityChangedEventArgs>? VisibilityChanged;

    /// <summary>Registers the shortcut and starts listening for it.</summary>
    Task<Result> StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops listening and releases the shortcut.</summary>
    Task StopAsync();

    /// <summary>Shows the launcher.</summary>
    Task ShowAsync(CancellationToken cancellationToken = default);

    /// <summary>Hides the launcher.</summary>
    Task HideAsync(CancellationToken cancellationToken = default);

    /// <summary>Shows a hidden launcher or hides a visible one.</summary>
    Task ToggleAsync(CancellationToken cancellationToken = default);
}
