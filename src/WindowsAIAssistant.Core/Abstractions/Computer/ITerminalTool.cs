using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Computer;

namespace WindowsAIAssistant.Core.Abstractions.Computer;

public interface ITerminalTool
{
    Task<Result<TerminalResult>> ExecuteAsync(
        string command,
        CancellationToken cancellationToken = default);
}
