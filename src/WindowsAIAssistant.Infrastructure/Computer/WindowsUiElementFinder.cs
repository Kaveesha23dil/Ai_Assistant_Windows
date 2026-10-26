using System.Runtime.InteropServices;
using System.Windows.Automation;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Computer;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Computer;

namespace WindowsAIAssistant.Infrastructure.Computer;

public sealed class WindowsUiElementFinder : IUiElementFinder
{
    private readonly IWindowManager _windows;
    private readonly UiElementRegistry _registry;
    private readonly ILogger<WindowsUiElementFinder> _logger;

    public WindowsUiElementFinder(
        IWindowManager windows,
        UiElementRegistry registry,
        ILogger<WindowsUiElementFinder> logger)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(logger);

        _windows = windows;
        _registry = registry;
        _logger = logger;
    }

    public async Task<Result<UiElementInfo>> FindAsync(
        UiElementQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var all = await FindAllAsync(query, cancellationToken).ConfigureAwait(false);
        if (all.IsFailure || all.Value is null)
        {
            return Result<UiElementInfo>.Failure(
                all.ErrorCode ?? ErrorCodes.ComputerTargetNotFound,
                all.ErrorMessage ?? "I couldn't find that element.");
        }

        return all.Value.Count == 0
            ? Result<UiElementInfo>.Failure(
                ErrorCodes.ComputerTargetNotFound,
                BuildNotFoundMessage(query))
            : Result<UiElementInfo>.Success(all.Value[0]);
    }

    public async Task<Result<IReadOnlyList<UiElementInfo>>> FindAllAsync(
        UiElementQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var root = await ResolveRootAsync(query, cancellationToken).ConfigureAwait(false);
        if (root.IsFailure || root.Value is null)
        {
            return Result<IReadOnlyList<UiElementInfo>>.Failure(
                root.ErrorCode ?? ErrorCodes.ComputerTargetNotFound,
                root.ErrorMessage ?? "I couldn't find that window.");
        }

        var deadline = DateTimeOffset.UtcNow + query.Timeout;
        IReadOnlyList<UiElementInfo> results;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            results = Scan(root.Value, query, cancellationToken);

            if (results.Count > 0 || DateTimeOffset.UtcNow >= deadline)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }
        while (true);

        if (results.Count == 0 && query.Name is not null)
        {
            var withoutName = query with { Name = null };
            var broad = Scan(root.Value, withoutName, cancellationToken);
            results = broad
                .Where(element =>
                    element.Name?.Contains(query.Name, StringComparison.OrdinalIgnoreCase) == true
                    || element.AutomationId?.Contains(query.Name, StringComparison.OrdinalIgnoreCase) == true)
                .ToArray();
        }

        if (query.Index > 0)
        {
            results = results.Skip(query.Index).ToArray();
        }

        return Result<IReadOnlyList<UiElementInfo>>.Success(results);
    }

    public Task<Result<UiElementInfo>> GetFocusedElementAsync(
        long? windowHandle = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null)
            {
                return Task.FromResult(Result<UiElementInfo>.Failure(
                    ErrorCodes.ComputerTargetNotFound,
                    "No element currently has keyboard focus."));
            }

            var expected = windowHandle is null ? 0 : new IntPtr(windowHandle.Value).ToInt64();
            var actual = focused.Current.NativeWindowHandle;
            if (expected != 0 && actual != 0 && actual != expected)
            {
                return Task.FromResult(Result<UiElementInfo>.Failure(
                    ErrorCodes.ComputerWrongActiveWindow,
                    "Keyboard focus is in a different window."));
            }

            var info = ToInfo(focused, depth: 0, windowHandle ?? 0);
            return Task.FromResult(info is null
                ? Result<UiElementInfo>.Failure(
                    ErrorCodes.ComputerTargetNotFound,
                    "I couldn't read the focused element.")
                : Result<UiElementInfo>.Success(info));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Reading the focused UI Automation element failed.");
            return Task.FromResult(Result<UiElementInfo>.Failure(
                ErrorCodes.ComputerActionFailed,
                "I couldn't read the focused element."));
        }
    }

    public Task<Result<IReadOnlyList<UiElementInfo>>> GetChildrenAsync(
        UiElementInfo element,
        bool includeOffscreen,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_registry.TryGet(element.Id, out var automation))
        {
            return Task.FromResult(Result<IReadOnlyList<UiElementInfo>>.Failure(
                ErrorCodes.ComputerTargetNotFound,
                "That element is no longer available."));
        }

        try
        {
            var children = new List<UiElementInfo>();
            var walker = TreeWalker.RawViewWalker;
            var child = walker.GetFirstChild(automation);

            while (child is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var info = ToInfo(child, element.Depth + 1, element.WindowHandle);
                if (info is not null && (includeOffscreen || !info.IsOffscreen))
                {
                    children.Add(info);
                }

                child = walker.GetNextSibling(child);
            }

            return Task.FromResult(Result<IReadOnlyList<UiElementInfo>>.Success(children));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Reading a UI Automation element's children failed.");
            return Task.FromResult(Result<IReadOnlyList<UiElementInfo>>.Failure(
                ErrorCodes.ComputerActionFailed,
                "I couldn't read that element's children."));
        }
    }

    private async Task<Result<AutomationElement>> ResolveRootAsync(
        UiElementQuery query,
        CancellationToken cancellationToken)
    {
        try
        {
            if (query.WindowHandle is { } handle && handle != 0)
            {
                return Result<AutomationElement>.Success(
                    AutomationElement.FromHandle(new IntPtr(handle)));
            }

            WindowInfo? window = null;
            if (!string.IsNullOrWhiteSpace(query.WindowTitle)
                || !string.IsNullOrWhiteSpace(query.ProcessName))
            {
                var found = await _windows
                    .FindWindowAsync(
                        query.WindowTitle ?? query.ProcessName!,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (found.IsFailure || found.Value is null)
                {
                    return Result<AutomationElement>.Failure(
                        ErrorCodes.ComputerTargetNotFound,
                        found.ErrorMessage ?? "I couldn't find that window.");
                }

                window = found.Value;
            }
            else
            {
                var active = await _windows.GetActiveWindowAsync(cancellationToken).ConfigureAwait(false);
                if (active.IsFailure || active.Value is null)
                {
                    return Result<AutomationElement>.Failure(
                        ErrorCodes.ComputerTargetNotFound,
                        active.ErrorMessage ?? "There is no active window.");
                }

                window = active.Value;
            }

            return Result<AutomationElement>.Success(
                AutomationElement.FromHandle(new IntPtr(window.Handle)));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Resolving the UI Automation root window failed.");
            return Result<AutomationElement>.Failure(
                ErrorCodes.ComputerActionFailed,
                "I couldn't read that window.");
        }
    }

    private IReadOnlyList<UiElementInfo> Scan(
        AutomationElement root,
        UiElementQuery query,
        CancellationToken cancellationToken)
    {
        var results = new List<UiElementInfo>();
        var depth = Math.Clamp(query.MaxDepth, 1, 30);
        var walker = TreeWalker.RawViewWalker;
        var queue = new Queue<(AutomationElement Element, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (element, level) = queue.Dequeue();
            if (level >= depth)
            {
                continue;
            }

            var child = walker.GetFirstChild(element);
            while (child is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var info = ToInfo(child, level + 1, root.Current.NativeWindowHandle);
                if (info is not null
                    && Matches(info, query)
                    && (query.IncludeOffscreen || !info.IsOffscreen))
                {
                    results.Add(info);
                }

                if (level + 1 < depth)
                {
                    queue.Enqueue((child, level + 1));
                }

                child = walker.GetNextSibling(child);
            }
        }

        return results;
    }

    private static bool Matches(UiElementInfo info, UiElementQuery query)
    {
        if (query.RequireEnabled && !info.IsEnabled)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.Name)
            && !string.Equals(info.Name, query.Name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.AutomationId)
            && !string.Equals(info.AutomationId, query.AutomationId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.ControlType)
            && !string.Equals(info.ControlType, query.ControlType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.ClassName)
            && !string.Equals(info.ClassName, query.ClassName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.Value)
            && !string.Equals(info.Value, query.Value, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private UiElementInfo? ToInfo(AutomationElement element, int depth, long fallbackWindow)
    {
        try
        {
            var current = element.Current;
            var windowHandle = current.NativeWindowHandle != 0
                ? current.NativeWindowHandle
                : fallbackWindow;
            var bounds = current.BoundingRectangle;
            var value = TryReadValue(element);
            var controlType = current.ControlType?.ProgrammaticName?.Replace(
                "ControlType.",
                string.Empty,
                StringComparison.Ordinal) ?? string.Empty;

            return new UiElementInfo
            {
                Id = _registry.Register(element),
                Name = string.IsNullOrWhiteSpace(current.Name) ? null : current.Name,
                AutomationId = string.IsNullOrWhiteSpace(current.AutomationId)
                    ? null
                    : current.AutomationId,
                ControlType = string.IsNullOrWhiteSpace(controlType) ? null : controlType,
                ClassName = string.IsNullOrWhiteSpace(current.ClassName) ? null : current.ClassName,
                Value = value,
                IsEnabled = current.IsEnabled,
                IsOffscreen = current.IsOffscreen,
                IsKeyboardFocusable = current.IsKeyboardFocusable,
                SupportsInvoke = element.TryGetCurrentPattern(InvokePattern.Pattern, out _),
                SupportsValuePattern = element.TryGetCurrentPattern(ValuePattern.Pattern, out _),
                SupportsSelectionItem = element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out _),
                Bounds = new ComputerRect(
                    (int)bounds.X,
                    (int)bounds.Y,
                    (int)bounds.Width,
                    (int)bounds.Height),
                WindowHandle = windowHandle,
                Depth = depth
            };
        }
        catch (Exception exception) when (exception is ElementNotAvailableException
            or InvalidOperationException
            or COMException)
        {
            return null;
        }
    }

    private static string? TryReadValue(AutomationElement element)
    {
        try
        {
            return element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern)
                && pattern is ValuePattern value
                ? value.Current.Value
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string BuildNotFoundMessage(UiElementQuery query)
    {
        var label = query.Name ?? query.AutomationId ?? query.ControlType ?? "that element";
        return $"I couldn't find {label} in the window.";
    }
}

