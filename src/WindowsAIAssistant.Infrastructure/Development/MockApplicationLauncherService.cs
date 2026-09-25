using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Common;

namespace WindowsAIAssistant.Infrastructure.Development;

public sealed class MockApplicationLauncherService : IApplicationLauncherService
{
    public Task<Result> LaunchAsync(
        string applicationName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(applicationName))
        {
            return Task.FromResult(Result.Failure("Application name cannot be empty."));
        }

        return Task.FromResult(Result.Success());
    }
}
