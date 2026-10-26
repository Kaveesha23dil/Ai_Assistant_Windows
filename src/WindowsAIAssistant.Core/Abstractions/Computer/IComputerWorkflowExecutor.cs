using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Computer;

namespace WindowsAIAssistant.Core.Abstractions.Computer;

public interface IComputerWorkflowExecutor
{
    Task<Result<ComputerWorkflowResult>> ExecuteAsync(
        ComputerWorkflow workflow,
        CancellationToken cancellationToken = default);
}
