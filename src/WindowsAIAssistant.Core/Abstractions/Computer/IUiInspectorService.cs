using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Computer;

namespace WindowsAIAssistant.Core.Abstractions.Computer;

public interface IUiInspectorService
{
    Task<Result<UiInspection>> InspectAsync(
        UiElementQuery query,
        CancellationToken cancellationToken = default);
}
