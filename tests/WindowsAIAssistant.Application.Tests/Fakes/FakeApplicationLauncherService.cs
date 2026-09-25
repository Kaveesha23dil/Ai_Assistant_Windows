using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Common;

namespace WindowsAIAssistant.Application.Tests.Fakes;

public sealed class FakeApplicationLauncherService : IApplicationLauncherService
{
    public Result LaunchResult { get; set; } = Result.Success();

    public string? LastApplicationName { get; private set; }

    public int CallCount { get; private set; }

    public Task<Result> LaunchAsync(
        string applicationName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        LastApplicationName = applicationName;
        return Task.FromResult(LaunchResult);
    }
}
