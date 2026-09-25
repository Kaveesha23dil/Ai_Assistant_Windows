using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Application.Tests.Fakes;

public sealed class FakeWindowsSystemService : IWindowsSystemService
{
    public SystemInformation Information { get; set; } = new(
        "TEST-MACHINE",
        "Test OS",
        "10.0.22621",
        "tester",
        8,
        16_000_000_000,
        8_000_000_000);

    public int CallCount { get; private set; }

    public Task<SystemInformation> GetSystemInformationAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        return Task.FromResult(Information);
    }
}
