using WindowsAIAssistant.Application.System.Commands.LaunchApplication;
using WindowsAIAssistant.Application.Tests.Fakes;
using WindowsAIAssistant.Core.Common;

namespace WindowsAIAssistant.Application.Tests;

public sealed class LaunchApplicationHandlerTests
{
    [Fact]
    public async Task HandleAsync_WithValidName_CallsLauncherAndReturnsResult()
    {
        var expectedResult = Result.Success();
        var service = new FakeApplicationLauncherService
        {
            LaunchResult = expectedResult
        };
        var handler = new LaunchApplicationHandler(service);

        var result = await handler.HandleAsync(new LaunchApplicationCommand("Calculator"));

        Assert.Same(expectedResult, result);
        Assert.Equal("Calculator", service.LastApplicationName);
        Assert.Equal(1, service.CallCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_WithEmptyName_ThrowsArgumentException(string applicationName)
    {
        var service = new FakeApplicationLauncherService();
        var handler = new LaunchApplicationHandler(service);

        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(new LaunchApplicationCommand(applicationName)));

        Assert.Equal(0, service.CallCount);
    }
}
