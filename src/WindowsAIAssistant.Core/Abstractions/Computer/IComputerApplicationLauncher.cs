using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Computer;

namespace WindowsAIAssistant.Core.Abstractions.Computer;

public interface IComputerApplicationLauncher
{
    Task<Result<ApplicationLaunchResult>> LaunchAsync(
        string applicationName,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
