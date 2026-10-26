using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Computer;

namespace WindowsAIAssistant.Core.Abstractions.Computer;

public interface IUiElementInteractor
{
    Task<Result> InvokeAsync(UiElementInfo element, CancellationToken cancellationToken = default);

    Task<Result> ClickAsync(
        UiElementInfo element,
        MouseButtonType button,
        bool doubleClick,
        CancellationToken cancellationToken = default);

    Task<Result> SetValueAsync(
        UiElementInfo element,
        string value,
        CancellationToken cancellationToken = default);

    Task<Result> FocusAsync(UiElementInfo element, CancellationToken cancellationToken = default);

    Task<Result> SelectAsync(UiElementInfo element, CancellationToken cancellationToken = default);

    Task<Result<string>> ReadTextAsync(
        UiElementInfo element,
        CancellationToken cancellationToken = default);
}
