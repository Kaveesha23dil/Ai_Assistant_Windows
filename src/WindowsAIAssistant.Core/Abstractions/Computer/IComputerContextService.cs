using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Computer;

namespace WindowsAIAssistant.Core.Abstractions.Computer;

public interface IComputerContextService
{
    Task<Result<ComputerContext>> GetContextAsync(CancellationToken cancellationToken = default);
}
