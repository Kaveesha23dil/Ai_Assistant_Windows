using System.Windows.Automation;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Computer;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Computer;

namespace WindowsAIAssistant.Infrastructure.Computer;

public sealed class WindowsUiElementInteractor : IUiElementInteractor
{
    private readonly UiElementRegistry _registry;
    private readonly IInputController _input;
    private readonly ILogger<WindowsUiElementInteractor> _logger;

    public WindowsUiElementInteractor(
        UiElementRegistry registry,
        IInputController input,
        ILogger<WindowsUiElementInteractor> logger)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(logger);

        _registry = registry;
        _input = input;
        _logger = logger;
    }

    public async Task<Result> InvokeAsync(
        UiElementInfo element,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_registry.TryGet(element.Id, out var automation))
        {
            return Missing();
        }

        try
        {
            if (automation.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern)
                && pattern is InvokePattern invoke)
            {
                invoke.Invoke();
                return Result.Success();
            }

            return await ClickAsync(element, MouseButtonType.Left, false, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Invoking a UI Automation element failed.");
            return Result.Failure(ErrorCodes.ComputerActionFailed, "I couldn't activate that element.");
        }
    }

    public async Task<Result> ClickAsync(
        UiElementInfo element,
        MouseButtonType button,
        bool doubleClick,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_registry.TryGet(element.Id, out var automation))
        {
            return Missing();
        }

        try
        {
            if (button == MouseButtonType.Left
                && !doubleClick
                && automation.TryGetCurrentPattern(InvokePattern.Pattern, out var invokePattern)
                && invokePattern is InvokePattern invoke)
            {
                invoke.Invoke();
                return Result.Success();
            }

            if (!TryGetClickPoint(automation, element, out var x, out var y))
            {
                return Result.Failure(
                    ErrorCodes.ComputerTargetNotFound,
                    "That element has no clickable point.");
            }

            return await _input.ClickAsync(
                new MouseInput
                {
                    X = x,
                    Y = y,
                    Button = button,
                    DoubleClick = doubleClick
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Clicking a UI Automation element failed.");
            return Result.Failure(ErrorCodes.ComputerActionFailed, "I couldn't click that element.");
        }
    }

    public Task<Result> SetValueAsync(
        UiElementInfo element,
        string value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_registry.TryGet(element.Id, out var automation))
        {
            return Task.FromResult(Missing());
        }

        try
        {
            if (automation.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern)
                && pattern is ValuePattern valuePattern
                && !valuePattern.Current.IsReadOnly)
            {
                valuePattern.SetValue(value);
                return Task.FromResult(Result.Success());
            }

            return Task.FromResult(Result.Failure(
                ErrorCodes.ComputerActionFailed,
                "That element does not accept typed text as a value."));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Setting a UI Automation value failed.");
            return Task.FromResult(Result.Failure(
                ErrorCodes.ComputerActionFailed,
                "I couldn't set that element's value."));
        }
    }

    public Task<Result> FocusAsync(
        UiElementInfo element,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_registry.TryGet(element.Id, out var automation))
        {
            return Task.FromResult(Missing());
        }

        try
        {
            automation.SetFocus();
            return Task.FromResult(Result.Success());
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Focusing a UI Automation element failed.");
            return Task.FromResult(Result.Failure(
                ErrorCodes.ComputerActionFailed,
                "I couldn't focus that element."));
        }
    }

    public Task<Result> SelectAsync(
        UiElementInfo element,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_registry.TryGet(element.Id, out var automation))
        {
            return Task.FromResult(Missing());
        }

        try
        {
            if (automation.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern)
                && pattern is SelectionItemPattern selection)
            {
                selection.Select();
                return Task.FromResult(Result.Success());
            }

            return Task.FromResult(Result.Failure(
                ErrorCodes.ComputerActionFailed,
                "That element cannot be selected."));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Selecting a UI Automation element failed.");
            return Task.FromResult(Result.Failure(
                ErrorCodes.ComputerActionFailed,
                "I couldn't select that element."));
        }
    }

    public Task<Result<string>> ReadTextAsync(
        UiElementInfo element,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_registry.TryGet(element.Id, out var automation))
        {
            return Task.FromResult(Result<string>.Failure(
                ErrorCodes.ComputerTargetNotFound,
                "That element is no longer available."));
        }

        try
        {
            if (automation.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePattern)
                && valuePattern is ValuePattern value)
            {
                return Task.FromResult(Result<string>.Success(value.Current.Value));
            }

            if (automation.TryGetCurrentPattern(TextPattern.Pattern, out var textPattern)
                && textPattern is TextPattern text)
            {
                return Task.FromResult(Result<string>.Success(
                    text.DocumentRange.GetText(2000)));
            }

            return Task.FromResult(Result<string>.Success(
                automation.Current.Name ?? string.Empty));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Reading text from a UI Automation element failed.");
            return Task.FromResult(Result<string>.Failure(
                ErrorCodes.ComputerActionFailed,
                "I couldn't read that element."));
        }
    }

    private static Result Missing() => Result.Failure(
        ErrorCodes.ComputerTargetNotFound,
        "That element is no longer available.");

    private static bool TryGetClickPoint(
        AutomationElement automation,
        UiElementInfo element,
        out int x,
        out int y)
    {
        x = 0;
        y = 0;

        try
        {
            if (automation.TryGetClickablePoint(out var point))
            {
                x = (int)point.X;
                y = (int)point.Y;
                return true;
            }
        }
        catch (Exception)
        {
            // Fall through to the bounding rectangle.
        }

        if (element.Bounds.IsEmpty)
        {
            return false;
        }

        x = element.Bounds.CenterX;
        y = element.Bounds.CenterY;
        return true;
    }
}
