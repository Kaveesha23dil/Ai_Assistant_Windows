using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Computer;

namespace WindowsAIAssistant.Core.Abstractions.Computer;

public interface IUiElementFinder
{
    Task<Result<UiElementInfo>> FindAsync(
        UiElementQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<UiElementInfo>>> FindAllAsync(
        UiElementQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<UiElementInfo>> GetFocusedElementAsync(
        long? windowHandle = null,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<UiElementInfo>>> GetChildrenAsync(
        UiElementInfo element,
        bool includeOffscreen,
        CancellationToken cancellationToken = default);
}
