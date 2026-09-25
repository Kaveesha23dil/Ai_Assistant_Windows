using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Infrastructure.Development;

public sealed class MockWindowsSystemService : IWindowsSystemService
{
    public Task<SystemInformation> GetSystemInformationAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new SystemInformation(
            "Development Machine",
            "Windows 11",
            "Development",
            "Development User",
            1,
            8_000_000_000,
            4_000_000_000));
    }
}
