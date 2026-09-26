using Microsoft.Extensions.Logging.Abstractions;
using WindowsAIAssistant.Application.System.Queries.GetSystemInformation;
using WindowsAIAssistant.Application.Tests.Fakes;

namespace WindowsAIAssistant.Application.Tests;

public sealed class GetSystemInformationHandlerTests
{
    [Fact]
    public async Task HandleAsync_ReturnsMappedSystemInformation()
    {
        var service = new FakeWindowsSystemService();
        var handler = new GetSystemInformationHandler(
            service,
            NullLogger<GetSystemInformationHandler>.Instance);

        var result = await handler.HandleAsync(new GetSystemInformationQuery());

        Assert.Equal("TEST-MACHINE", result.MachineName);
        Assert.Equal("Test OS", result.OperatingSystem);
        Assert.Equal("10.0.22621", result.OperatingSystemVersion);
        Assert.Equal("tester", result.UserName);
        Assert.Equal(8, result.ProcessorCount);
        Assert.Equal(16_000_000_000, result.TotalMemory);
        Assert.Equal(8_000_000_000, result.AvailableMemory);
        Assert.Equal(1, service.CallCount);
    }
}
