using Microsoft.Extensions.Logging.Abstractions;
using WindowsAIAssistant.Application.AI.Commands.SendMessage;
using WindowsAIAssistant.Application.Tests.Fakes;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Application.Tests;

public sealed class SendMessageHandlerTests
{
    [Fact]
    public async Task HandleAsync_WithValidMessage_CallsServiceAndReturnsResponse()
    {
        var service = new FakeAIService();
        var handler = new SendMessageHandler(service, NullLogger<SendMessageHandler>.Instance);

        var response = await handler.HandleAsync(new SendMessageCommand("Hello"));

        Assert.Same(service.Response, response);
        Assert.Equal(1, service.CallCount);
        var message = Assert.Single(service.LastMessages!);
        Assert.Equal(AIMessageRole.User, message.Role);
        Assert.Equal("Hello", message.Content);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_WithEmptyMessage_ThrowsArgumentException(string message)
    {
        var service = new FakeAIService();
        var handler = new SendMessageHandler(service, NullLogger<SendMessageHandler>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(new SendMessageCommand(message)));

        Assert.Equal(0, service.CallCount);
    }
}
