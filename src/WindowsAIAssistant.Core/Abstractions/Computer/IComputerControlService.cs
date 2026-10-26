using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Computer;

namespace WindowsAIAssistant.Core.Abstractions.Computer;

public interface IComputerControlService
{
    Task<Result<ComputerActionResult>> ExecuteAsync(
        ComputerActionRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<ComputerContext>> GetContextAsync(CancellationToken cancellationToken = default);

    void CancelCurrent();

    bool IsBusy { get; }
}
