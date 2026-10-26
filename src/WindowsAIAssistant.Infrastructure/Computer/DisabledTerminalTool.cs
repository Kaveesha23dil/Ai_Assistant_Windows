using WindowsAIAssistant.Core.Abstractions.Computer;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Computer;

namespace WindowsAIAssistant.Infrastructure.Computer;

public sealed class DisabledTerminalTool : ITerminalTool
{
    public Task<Result<TerminalResult>> ExecuteAsync(
        string command,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<TerminalResult>.Failure(
            ErrorCodes.ComputerTerminalDisabled,
            "Running terminal commands is not available. This feature controls Windows applications through UI Automation only."));
    }
}
