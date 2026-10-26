using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.Computer;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Computer;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.Computer;

public sealed class WindowsComputerControlService : IComputerControlService, IDisposable
{
    private readonly IComputerActionValidator _validator;
    private readonly IComputerPermissionService _permissions;
    private readonly IComputerApplicationLauncher _launcher;
    private readonly IWindowManager _windows;
    private readonly IInputController _input;
    private readonly IUiElementFinder _finder;
    private readonly IUiElementInteractor _interactor;
    private readonly IComputerContextService _context;
    private readonly IOptionsMonitor<ComputerControlOptions> _options;
    private readonly ILogger<WindowsComputerControlService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Queue<DateTimeOffset> _recentActions = new();
    private readonly object _rateLock = new();

    private CancellationTokenSource? _current;
    private bool _disposed;

    public WindowsComputerControlService(
        IComputerActionValidator validator,
        IComputerPermissionService permissions,
        IComputerApplicationLauncher launcher,
        IWindowManager windows,
        IInputController input,
        IUiElementFinder finder,
        IUiElementInteractor interactor,
        IComputerContextService context,
        IOptionsMonitor<ComputerControlOptions> options,
        ILogger<WindowsComputerControlService> logger)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(finder);
        ArgumentNullException.ThrowIfNull(interactor);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _validator = validator;
        _permissions = permissions;
        _launcher = launcher;
        _windows = windows;
        _input = input;
        _finder = finder;
        _interactor = interactor;
        _context = context;
        _options = options;
        _logger = logger;
    }

    public bool IsBusy => _gate.CurrentCount == 0;

    public async Task<Result<ComputerActionResult>> ExecuteAsync(
        ComputerActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await _gate.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
        {
            return Result<ComputerActionResult>.Failure(
                ErrorCodes.ComputerBusy,
                "Another computer action is already running.");
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _current = linked;

        try
        {
            var safety = _options.CurrentValue.ToSafetyContext();
            var validation = _validator.Validate(request, safety);
            if (validation.IsFailure)
            {
                return Result<ComputerActionResult>.Failure(
                    validation.ErrorCode ?? ErrorCodes.ComputerPermissionDenied,
                    validation.ErrorMessage ?? "That computer action is not allowed.");
            }

            var permission = _permissions.EnsureAllowed(request.Action.Type, request.IsConfirmed);
            if (permission.IsFailure)
            {
                return Result<ComputerActionResult>.Failure(
                    permission.ErrorCode ?? ErrorCodes.ComputerPermissionDenied,
                    permission.ErrorMessage ?? "That computer action is not allowed.");
            }

            if (request.Action.Type == ComputerActionType.LaunchApplication)
            {
                var application = GetApplicationName(request.Action);
                var appPermission = _permissions.EnsureApplicationAllowed(application);
                if (appPermission.IsFailure)
                {
                    return Result<ComputerActionResult>.Failure(
                        appPermission.ErrorCode ?? ErrorCodes.ComputerPermissionDenied,
                        appPermission.ErrorMessage ?? "That application is not allowed.");
                }
            }

            if (!TryReserveRateSlot())
            {
                return Result<ComputerActionResult>.Failure(
                    ErrorCodes.ComputerRateLimited,
                    "Too many computer actions were requested in a short time. Wait a moment and try again.");
            }

            var actionResult = await ExecuteWithRetryAsync(request, safety, linked.Token)
                .ConfigureAwait(false);

            if (actionResult.IsFailure)
            {
                return actionResult;
            }

            return Result<ComputerActionResult>.Success(actionResult.Value!);
        }
        catch (OperationCanceledException)
        {
            return Result<ComputerActionResult>.Failure(
                ErrorCodes.ComputerCancelled,
                "The computer action was stopped.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "A computer control action failed unexpectedly.");
            return Result<ComputerActionResult>.Failure(
                ErrorCodes.ComputerActionFailed,
                "The computer action could not be completed.");
        }
        finally
        {
            _current = null;
            linked.Dispose();
            _gate.Release();
        }
    }

    public Task<Result<ComputerContext>> GetContextAsync(
        CancellationToken cancellationToken = default) =>
        _context.GetContextAsync(cancellationToken);

    public void CancelCurrent()
    {
        try
        {
            _current?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The action finished between the check and the cancel.
        }
    }

    private async Task<Result<ComputerActionResult>> ExecuteWithRetryAsync(
        ComputerActionRequest request,
        ComputerSafetyContext safety,
        CancellationToken cancellationToken)
    {
        var retries = IsRetryable(request.Action.Type)
            ? Math.Clamp(request.Action.MaxRetries, 0, 2)
            : 0;

        Result<ComputerActionResult> result = Result<ComputerActionResult>.Failure(
            ErrorCodes.ComputerActionFailed,
            "The computer action did not run.");

        for (var attempt = 0; attempt <= retries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result = await ExecuteOnceAsync(request, safety, cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                return result;
            }

            if (!IsRetryable(request.Action.Type) || attempt == retries)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    private async Task<Result<ComputerActionResult>> ExecuteOnceAsync(
        ComputerActionRequest request,
        ComputerSafetyContext safety,
        CancellationToken cancellationToken)
    {
        var action = request.Action;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            return action.Type switch
            {
                ComputerActionType.LaunchApplication => await LaunchAsync(action, stopwatch, cancellationToken)
                    .ConfigureAwait(false),
                ComputerActionType.CloseApplication => await WindowActionAsync(
                    action,
                    _windows.CloseAsync,
                    "Closed the window.",
                    stopwatch,
                    cancellationToken).ConfigureAwait(false),
                ComputerActionType.SwitchWindow => await SwitchAsync(action, stopwatch, cancellationToken)
                    .ConfigureAwait(false),
                ComputerActionType.MinimizeWindow => await WindowActionAsync(
                    action,
                    _windows.MinimizeAsync,
                    "Minimized the window.",
                    stopwatch,
                    cancellationToken).ConfigureAwait(false),
                ComputerActionType.MaximizeWindow => await WindowActionAsync(
                    action,
                    _windows.MaximizeAsync,
                    "Maximized the window.",
                    stopwatch,
                    cancellationToken).ConfigureAwait(false),
                ComputerActionType.RestoreWindow => await WindowActionAsync(
                    action,
                    _windows.RestoreAsync,
                    "Restored the window.",
                    stopwatch,
                    cancellationToken).ConfigureAwait(false),
                ComputerActionType.Click or
                ComputerActionType.DoubleClick or
                ComputerActionType.RightClick => await ClickAsync(action, stopwatch, cancellationToken)
                    .ConfigureAwait(false),
                ComputerActionType.TypeText => await TypeAsync(action, stopwatch, cancellationToken)
                    .ConfigureAwait(false),
                ComputerActionType.PressKey or ComputerActionType.PressShortcut =>
                    await PressAsync(action, stopwatch, cancellationToken).ConfigureAwait(false),
                ComputerActionType.Scroll => await ScrollAsync(action, stopwatch, cancellationToken)
                    .ConfigureAwait(false),
                ComputerActionType.Select => await SelectAsync(action, stopwatch, cancellationToken)
                    .ConfigureAwait(false),
                ComputerActionType.Focus => await FocusAsync(action, stopwatch, cancellationToken)
                    .ConfigureAwait(false),
                ComputerActionType.ReadUiElement => await ReadAsync(action, stopwatch, cancellationToken)
                    .ConfigureAwait(false),
                ComputerActionType.FindUiElement => await FindAsync(action, stopwatch, cancellationToken)
                    .ConfigureAwait(false),
                ComputerActionType.Wait => await WaitAsync(action, stopwatch, cancellationToken)
                    .ConfigureAwait(false),
                _ => Result<ComputerActionResult>.Failure(
                    ErrorCodes.ComputerUnsafeAction,
                    "That computer action is not available.")
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Computer action {Action} failed.", action.Type);
            return Result<ComputerActionResult>.Failure(
                ErrorCodes.ComputerActionFailed,
                "The computer action could not be completed.");
        }
    }

    private async Task<Result<ComputerActionResult>> LaunchAsync(
        ComputerAction action,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var application = GetApplicationName(action);
        var timeout = NormalizeTimeout(action.Timeout, _options.CurrentValue.ActionTimeoutSeconds);

        var launched = await _launcher.LaunchAsync(application, timeout, cancellationToken)
            .ConfigureAwait(false);

        stopwatch.Stop();
        if (launched.IsFailure || launched.Value is null)
        {
            return Result<ComputerActionResult>.Failure(
                launched.ErrorCode ?? ErrorCodes.ComputerActionFailed,
                launched.ErrorMessage ?? $"I couldn't open {application}.",
                stopwatch.Elapsed);
        }

        return Result<ComputerActionResult>.Success(ComputerActionResult.Success(
            action.Type,
            $"Opened {launched.Value.ApplicationName}.",
            stopwatch.Elapsed,
            data: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["application"] = launched.Value.ApplicationName,
                ["processId"] = launched.Value.ProcessId?.ToString() ?? string.Empty
            }));
    }

    private async Task<Result<ComputerActionResult>> SwitchAsync(
        ComputerAction action,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var window = await ResolveWindowAsync(action, cancellationToken).ConfigureAwait(false);
        if (window.IsFailure || window.Value is null)
        {
            return WindowFailure(action, window, stopwatch);
        }

        var switched = await _windows.SwitchToAsync(window.Value, cancellationToken)
            .ConfigureAwait(false);

        stopwatch.Stop();
        if (switched.IsFailure)
        {
            return Result<ComputerActionResult>.Failure(
                switched.ErrorCode ?? ErrorCodes.ComputerActionFailed,
                switched.ErrorMessage ?? "I couldn't switch windows.",
                stopwatch.Elapsed);
        }

        return Result<ComputerActionResult>.Success(ComputerActionResult.Success(
            action.Type,
            $"Switched to {window.Value.Label}.",
            stopwatch.Elapsed,
            activeWindow: window.Value,
            verification: "The window is now in the foreground."));
    }

    private async Task<Result<ComputerActionResult>> WindowActionAsync(
        ComputerAction action,
        Func<WindowInfo, CancellationToken, Task<Result>> operation,
        string successMessage,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var window = await ResolveWindowAsync(action, cancellationToken).ConfigureAwait(false);
        if (window.IsFailure || window.Value is null)
        {
            return WindowFailure(action, window, stopwatch);
        }

        var result = await operation(window.Value, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        return result.IsFailure
            ? Result<ComputerActionResult>.Failure(
                result.ErrorCode ?? ErrorCodes.ComputerActionFailed,
                result.ErrorMessage ?? "The window action failed.",
                stopwatch.Elapsed)
            : Result<ComputerActionResult>.Success(ComputerActionResult.Success(
                action.Type,
                successMessage,
                stopwatch.Elapsed,
                activeWindow: window.Value));
    }

    private async Task<Result<ComputerActionResult>> ClickAsync(
        ComputerAction action,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        if (action.Target is null)
        {
            if (!action.HasCoordinates)
            {
                return Result<ComputerActionResult>.Failure(
                    ErrorCodes.ComputerTargetNotFound,
                    "No element or coordinates were given for the click.");
            }

            var clicked = await _input.ClickAsync(
                new MouseInput
                {
                    X = action.X,
                    Y = action.Y,
                    Button = action.MouseButton,
                    DoubleClick = action.Type == ComputerActionType.DoubleClick
                },
                cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();
            return clicked.IsFailure
                ? Result<ComputerActionResult>.Failure(
                    clicked.ErrorCode ?? ErrorCodes.ComputerActionFailed,
                    clicked.ErrorMessage ?? "I couldn't click there.",
                    stopwatch.Elapsed)
                : Result<ComputerActionResult>.Success(ComputerActionResult.Success(
                    action.Type,
                    "Clicked the screen position.",
                    stopwatch.Elapsed));
        }

        var element = await FindElementAsync(action, cancellationToken).ConfigureAwait(false);
        if (element.IsFailure || element.Value is null)
        {
            return ElementFailure(action, element, stopwatch);
        }

        var active = await EnsureElementWindowActiveAsync(element.Value, cancellationToken)
            .ConfigureAwait(false);
        if (active.IsFailure)
        {
            return Result<ComputerActionResult>.Failure(
                active.ErrorCode ?? ErrorCodes.ComputerWrongActiveWindow,
                active.ErrorMessage ?? "The target window is not active.",
                stopwatch.Elapsed);
        }

        var result = await _interactor.ClickAsync(
            element.Value,
            action.MouseButton,
            action.Type == ComputerActionType.DoubleClick,
            cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();
        return result.IsFailure
            ? Result<ComputerActionResult>.Failure(
                result.ErrorCode ?? ErrorCodes.ComputerActionFailed,
                result.ErrorMessage ?? "I couldn't click that element.",
                stopwatch.Elapsed)
            : Result<ComputerActionResult>.Success(ComputerActionResult.Success(
                action.Type,
                $"Clicked {element.Value.Label}.",
                stopwatch.Elapsed,
                element: element.Value,
                verification: "The UI Automation element accepted the click."));
    }

    private async Task<Result<ComputerActionResult>> TypeAsync(
        ComputerAction action,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var text = action.Text ?? string.Empty;

        if (action.Target is not null)
        {
            var element = await FindElementAsync(action, cancellationToken).ConfigureAwait(false);
            if (element.IsFailure || element.Value is null)
            {
                return ElementFailure(action, element, stopwatch);
            }

            var active = await EnsureElementWindowActiveAsync(element.Value, cancellationToken)
                .ConfigureAwait(false);
            if (active.IsFailure)
            {
                return Result<ComputerActionResult>.Failure(
                    active.ErrorCode ?? ErrorCodes.ComputerWrongActiveWindow,
                    active.ErrorMessage ?? "The target window is not active.",
                    stopwatch.Elapsed);
            }

            var focused = await _interactor.FocusAsync(element.Value, cancellationToken)
                .ConfigureAwait(false);
            if (focused.IsFailure)
            {
                return Result<ComputerActionResult>.Failure(
                    focused.ErrorCode ?? ErrorCodes.ComputerActionFailed,
                    focused.ErrorMessage ?? "I couldn't focus the text field.",
                    stopwatch.Elapsed);
            }
        }

        var typed = await _input.TypeTextAsync(text, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        return typed.IsFailure
            ? Result<ComputerActionResult>.Failure(
                typed.ErrorCode ?? ErrorCodes.ComputerActionFailed,
                typed.ErrorMessage ?? "I couldn't type the text.",
                stopwatch.Elapsed)
            : Result<ComputerActionResult>.Success(ComputerActionResult.Success(
                action.Type,
                $"Typed {text.Length} characters.",
                stopwatch.Elapsed,
                data: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["characters"] = text.Length.ToString()
                },
                verification: "The keystrokes were delivered to the active window."));
    }

    private async Task<Result<ComputerActionResult>> PressAsync(
        ComputerAction action,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        if (action.Target is not null)
        {
            var element = await FindElementAsync(action, cancellationToken).ConfigureAwait(false);
            if (element.IsFailure || element.Value is null)
            {
                return ElementFailure(action, element, stopwatch);
            }

            var active = await EnsureElementWindowActiveAsync(element.Value, cancellationToken)
                .ConfigureAwait(false);
            if (active.IsFailure)
            {
                return Result<ComputerActionResult>.Failure(
                    active.ErrorCode ?? ErrorCodes.ComputerWrongActiveWindow,
                    active.ErrorMessage ?? "The target window is not active.",
                    stopwatch.Elapsed);
            }

            _ = await _interactor.FocusAsync(element.Value, cancellationToken).ConfigureAwait(false);
        }

        var keys = action.Keys.Count > 0
            ? action.Keys
            : action.Key is null ? [] : [action.Key];

        var result = action.Type == ComputerActionType.PressShortcut || keys.Count > 1
            ? await _input.PressShortcutAsync(keys, cancellationToken).ConfigureAwait(false)
            : await _input.PressKeyAsync(new KeyboardInput { Key = action.Key }, cancellationToken)
                .ConfigureAwait(false);

        stopwatch.Stop();
        return result.IsFailure
            ? Result<ComputerActionResult>.Failure(
                result.ErrorCode ?? ErrorCodes.ComputerActionFailed,
                result.ErrorMessage ?? "I couldn't press that key.",
                stopwatch.Elapsed)
            : Result<ComputerActionResult>.Success(ComputerActionResult.Success(
                action.Type,
                "Pressed the key.",
                stopwatch.Elapsed));
    }

    private async Task<Result<ComputerActionResult>> ScrollAsync(
        ComputerAction action,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var delta = action.ScrollDelta == 0 ? -120 : action.ScrollDelta;
        var result = await _input.ScrollAsync(delta, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        return result.IsFailure
            ? Result<ComputerActionResult>.Failure(
                result.ErrorCode ?? ErrorCodes.ComputerActionFailed,
                result.ErrorMessage ?? "I couldn't scroll.",
                stopwatch.Elapsed)
            : Result<ComputerActionResult>.Success(ComputerActionResult.Success(
                action.Type,
                "Scrolled the active window.",
                stopwatch.Elapsed));
    }

    private async Task<Result<ComputerActionResult>> SelectAsync(
        ComputerAction action,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var element = await FindElementAsync(action, cancellationToken).ConfigureAwait(false);
        if (element.IsFailure || element.Value is null)
        {
            return ElementFailure(action, element, stopwatch);
        }

        var active = await EnsureElementWindowActiveAsync(element.Value, cancellationToken)
            .ConfigureAwait(false);
        if (active.IsFailure)
        {
            return Result<ComputerActionResult>.Failure(
                active.ErrorCode ?? ErrorCodes.ComputerWrongActiveWindow,
                active.ErrorMessage ?? "The target window is not active.",
                stopwatch.Elapsed);
        }

        var result = await _interactor.SelectAsync(element.Value, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        return result.IsFailure
            ? Result<ComputerActionResult>.Failure(
                result.ErrorCode ?? ErrorCodes.ComputerActionFailed,
                result.ErrorMessage ?? "I couldn't select that element.",
                stopwatch.Elapsed)
            : Result<ComputerActionResult>.Success(ComputerActionResult.Success(
                action.Type,
                $"Selected {element.Value.Label}.",
                stopwatch.Elapsed,
                element: element.Value));
    }

    private async Task<Result<ComputerActionResult>> FocusAsync(
        ComputerAction action,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var element = await FindElementAsync(action, cancellationToken).ConfigureAwait(false);
        if (element.IsFailure || element.Value is null)
        {
            return ElementFailure(action, element, stopwatch);
        }

        var active = await EnsureElementWindowActiveAsync(element.Value, cancellationToken)
            .ConfigureAwait(false);
        if (active.IsFailure)
        {
            return Result<ComputerActionResult>.Failure(
                active.ErrorCode ?? ErrorCodes.ComputerWrongActiveWindow,
                active.ErrorMessage ?? "The target window is not active.",
                stopwatch.Elapsed);
        }

        var result = await _interactor.FocusAsync(element.Value, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        return result.IsFailure
            ? Result<ComputerActionResult>.Failure(
                result.ErrorCode ?? ErrorCodes.ComputerActionFailed,
                result.ErrorMessage ?? "I couldn't focus that element.",
                stopwatch.Elapsed)
            : Result<ComputerActionResult>.Success(ComputerActionResult.Success(
                action.Type,
                $"Focused {element.Value.Label}.",
                stopwatch.Elapsed,
                element: element.Value));
    }

    private async Task<Result<ComputerActionResult>> ReadAsync(
        ComputerAction action,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var element = await FindElementAsync(action, cancellationToken).ConfigureAwait(false);
        if (element.IsFailure || element.Value is null)
        {
            return ElementFailure(action, element, stopwatch);
        }

        var read = await _interactor.ReadTextAsync(element.Value, cancellationToken)
            .ConfigureAwait(false);

        stopwatch.Stop();
        if (read.IsFailure || read.Value is null)
        {
            return Result<ComputerActionResult>.Failure(
                read.ErrorCode ?? ErrorCodes.ComputerActionFailed,
                read.ErrorMessage ?? "I couldn't read that element.",
                stopwatch.Elapsed);
        }

        return Result<ComputerActionResult>.Success(ComputerActionResult.Success(
            action.Type,
            $"Read {read.Value.Length} characters from {element.Value.Label}.",
            stopwatch.Elapsed,
            element: element.Value,
            data: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["text"] = read.Value,
                ["length"] = read.Value.Length.ToString()
            }));
    }

    private async Task<Result<ComputerActionResult>> FindAsync(
        ComputerAction action,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var element = await FindElementAsync(action, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        if (element.IsFailure || element.Value is null)
        {
            return ElementFailure(action, element, stopwatch);
        }

        return Result<ComputerActionResult>.Success(ComputerActionResult.Success(
            action.Type,
            $"Found {element.Value.Label}.",
            stopwatch.Elapsed,
            element: element.Value,
            data: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["element"] = element.Value.Label,
                ["controlType"] = element.Value.ControlType ?? string.Empty,
                ["automationId"] = element.Value.AutomationId ?? string.Empty
            }));
    }

    private async Task<Result<ComputerActionResult>> WaitAsync(
        ComputerAction action,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var wait = NormalizeTimeout(action.Timeout, 5);
        await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        return Result<ComputerActionResult>.Success(ComputerActionResult.Success(
            action.Type,
            $"Waited {wait.TotalSeconds:0.#} seconds.",
            stopwatch.Elapsed));
    }

    private async Task<Result<WindowInfo>> ResolveWindowAsync(
        ComputerAction action,
        CancellationToken cancellationToken)
    {
        var target = action.Target;
        var name = target?.WindowTitle ?? target?.ProcessName ?? target?.ApplicationName;

        if (!string.IsNullOrWhiteSpace(name))
        {
            return await _windows.FindWindowAsync(name, cancellationToken).ConfigureAwait(false);
        }

        return await _windows.GetActiveWindowAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<UiElementInfo>> FindElementAsync(
        ComputerAction action,
        CancellationToken cancellationToken)
    {
        var target = action.Target;
        var query = new UiElementQuery
        {
            WindowTitle = target?.WindowTitle,
            ProcessName = target?.ProcessName,
            Name = target?.ElementName,
            AutomationId = target?.AutomationId,
            ControlType = target?.ControlType,
            ClassName = target?.ClassName,
            MaxDepth = 12,
            Timeout = NormalizeTimeout(action.Timeout, _options.CurrentValue.ActionTimeoutSeconds),
            UseCoordinateFallback = action.UseCoordinateFallback
        };

        return await _finder.FindAsync(query, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result> EnsureElementWindowActiveAsync(
        UiElementInfo element,
        CancellationToken cancellationToken)
    {
        if (element.WindowHandle == 0)
        {
            return Result.Success();
        }

        var active = await _windows.GetActiveWindowAsync(cancellationToken).ConfigureAwait(false);
        if (active.IsFailure || active.Value is null)
        {
            return Result.Failure(
                ErrorCodes.ComputerWrongActiveWindow,
                "There is no active window to act on.");
        }

        return active.Value.Handle == element.WindowHandle
            ? Result.Success()
            : Result.Failure(
                ErrorCodes.ComputerWrongActiveWindow,
                "The target element's window is not the active window.");
    }

    private bool TryReserveRateSlot()
    {
        var limit = Math.Max(_options.CurrentValue.MaxActionsPerMinute, 1);
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1);

        lock (_rateLock)
        {
            while (_recentActions.Count > 0 && _recentActions.Peek() < cutoff)
            {
                _recentActions.Dequeue();
            }

            if (_recentActions.Count >= limit)
            {
                return false;
            }

            _recentActions.Enqueue(DateTimeOffset.UtcNow);
            return true;
        }
    }

    private static string GetApplicationName(ComputerAction action) =>
        action.Target?.ApplicationName ?? action.Text ?? string.Empty;

    private static bool IsRetryable(ComputerActionType actionType) => actionType is
        ComputerActionType.SwitchWindow
        or ComputerActionType.FindUiElement
        or ComputerActionType.ReadUiElement
        or ComputerActionType.Focus;

    private static TimeSpan NormalizeTimeout(TimeSpan value, int fallbackSeconds)
    {
        var seconds = value == default || value == Timeout.InfiniteTimeSpan
            ? Math.Clamp(fallbackSeconds, 1, 60)
            : Math.Clamp(value.TotalSeconds, 1, 60);

        return TimeSpan.FromSeconds(seconds);
    }

    private static Result<ComputerActionResult> WindowFailure(
        ComputerAction action,
        Result<WindowInfo> window,
        Stopwatch stopwatch)
    {
        stopwatch.Stop();
        return Result<ComputerActionResult>.Failure(
            window.ErrorCode ?? ErrorCodes.ComputerTargetNotFound,
            window.ErrorMessage ?? "I couldn't find that window.",
            stopwatch.Elapsed);
    }

    private static Result<ComputerActionResult> ElementFailure(
        ComputerAction action,
        Result<UiElementInfo> element,
        Stopwatch stopwatch)
    {
        stopwatch.Stop();
        return Result<ComputerActionResult>.Failure(
            element.ErrorCode ?? ErrorCodes.ComputerTargetNotFound,
            element.ErrorMessage ?? "I couldn't find that element.",
            stopwatch.Elapsed);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _current?.Cancel();
        _current?.Dispose();
        _gate.Dispose();
    }
}
