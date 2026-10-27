using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.Launcher;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Launcher;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.Launcher;

/// <summary>
/// Keeps the launcher's logical visibility and owns the lifetime of its global shortcut.
/// </summary>
public sealed class AssistantLauncherService : IAssistantLauncherService, IDisposable
{
    private readonly IGlobalHotkeyService _hotkeys;
    private readonly IOptionsMonitor<LauncherOptions> _options;
    private readonly ILogger<AssistantLauncherService> _logger;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly IDisposable? _optionsSubscription;

    private bool _started;
    private bool _isVisible;
    private string? _activeHotkey;
    private Result? _hotkeyError;

    public AssistantLauncherService(
        IGlobalHotkeyService hotkeys,
        IOptionsMonitor<LauncherOptions> options,
        ILogger<AssistantLauncherService> logger)
    {
        ArgumentNullException.ThrowIfNull(hotkeys);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _hotkeys = hotkeys;
        _options = options;
        _logger = logger;

        _optionsSubscription = options.OnChange(OnOptionsChanged);
    }

    /// <inheritdoc />
    public bool IsVisible
    {
        get
        {
            lock (_lifecycle)
            {
                return _isVisible;
            }
        }
    }

    /// <inheritdoc />
    public string? ActiveHotkey
    {
        get
        {
            lock (_lifecycle)
            {
                return _activeHotkey;
            }
        }
    }

    /// <inheritdoc />
    public Result? HotkeyRegistrationError
    {
        get
        {
            lock (_lifecycle)
            {
                return _hotkeyError;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<LauncherVisibilityChangedEventArgs>? VisibilityChanged;

    /// <inheritdoc />
    public async Task<Result> StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_started)
            {
                _hotkeys.Pressed += OnHotkeyPressed;
                _started = true;
            }

            return await ApplyHotkeyAsync(_options.CurrentValue, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_started)
            {
                _hotkeys.Pressed -= OnHotkeyPressed;
                _started = false;
            }

            await _hotkeys.UnregisterAsync().ConfigureAwait(false);
            _activeHotkey = null;
            _hotkeyError = null;
            SetVisible(false, "Stopped");
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <inheritdoc />
    public Task ShowAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetVisible(true, "Request");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task HideAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetVisible(false, "Request");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ToggleAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetVisible(!IsVisible, "Request");
        return Task.CompletedTask;
    }

    private async Task<Result> ApplyHotkeyAsync(
        LauncherOptions options,
        CancellationToken cancellationToken)
    {
        var previous = _activeHotkey;
        var previousError = _hotkeyError;

        if (!options.Enabled)
        {
            await _hotkeys.UnregisterAsync(cancellationToken).ConfigureAwait(false);
            _activeHotkey = null;
            _hotkeyError = null;
            return Result.Success();
        }

        if (!HotkeyGesture.TryParse(options.Hotkey, out var hotkey, out var parseError)
            || hotkey is null)
        {
            var failure = Result.Failure(
                ErrorCodes.LauncherHotkeyInvalid,
                $"Choose a shortcut such as Alt+Space. {parseError}");
            _hotkeyError = failure;
            _logger.LogWarning("The configured launcher shortcut is invalid.");
            return failure;
        }

        var registered = await _hotkeys
            .RegisterAsync(hotkey, cancellationToken)
            .ConfigureAwait(false);

        if (registered.IsFailure)
        {
            _hotkeyError = registered;
            _logger.LogWarning("The launcher shortcut could not be registered.");

            if (!string.IsNullOrWhiteSpace(previous)
                && HotkeyGesture.TryParse(previous, out var previousGesture, out _)
                && previousGesture is not null)
            {
                var restored = await _hotkeys
                    .RegisterAsync(previousGesture, cancellationToken)
                    .ConfigureAwait(false);

                if (restored.IsSuccess)
                {
                    _activeHotkey = previous;
                    _hotkeyError = registered;
                    return registered;
                }
            }

            _activeHotkey = null;
            _hotkeyError = registered;
            return registered;
        }

        _activeHotkey = hotkey.DisplayName;
        _hotkeyError = previousError is not null && previous == _activeHotkey ? previousError : null;
        _logger.LogInformation("Launcher shortcut registered as {Hotkey}.", _activeHotkey);
        return registered;
    }

    private void OnHotkeyPressed(object? sender, EventArgs args)
    {
        var show = !IsVisible;
        SetVisible(show, "Hotkey");
    }

    private void OnOptionsChanged(LauncherOptions options)
    {
        _ = ApplyChangedOptionsAsync(options);
    }

    private async Task ApplyChangedOptionsAsync(LauncherOptions options)
    {
        try
        {
            await _lifecycle.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!_started)
                {
                    return;
                }

                await ApplyHotkeyAsync(options, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _lifecycle.Release();
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Applying a new launcher shortcut failed.");
        }
    }

    private void SetVisible(bool visible, string reason)
    {
        lock (_lifecycle)
        {
            if (_isVisible == visible)
            {
                return;
            }

            _isVisible = visible;
        }

        VisibilityChanged?.Invoke(this, new LauncherVisibilityChangedEventArgs(visible, reason));
    }

    public void Dispose()
    {
        _optionsSubscription?.Dispose();
        _hotkeys.Pressed -= OnHotkeyPressed;
        _lifecycle.Dispose();
    }
}
