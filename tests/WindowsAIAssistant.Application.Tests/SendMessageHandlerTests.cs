using Microsoft.Extensions.Logging.Abstractions;
using WindowsAIAssistant.Application.AI.Commands.SendMessage;
using WindowsAIAssistant.Application.Tests.Fakes;
using WindowsAIAssistant.Application.Tests.Helpers;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Application.Tests;

public sealed class SendMessageHandlerTests
{
    private static SendMessageHandler CreateHandler(
        FakeAIService service,
        Microsoft.Extensions.Logging.ILogger<SendMessageHandler>? logger = null) =>
        new(
            service,
            AITestHarness.CreateContextBuilder(),
            AITestHarness.CreateConversations(),
            logger ?? NullLogger<SendMessageHandler>.Instance);

    [Fact]
    public async Task HandleAsync_WithValidMessage_CallsServiceAndReturnsResponse()
    {
        var service = new FakeAIService();
        var handler = CreateHandler(service);

        var response = await handler.HandleAsync(new SendMessageCommand("Hello"));

        Assert.Same(service.Response, response);
        Assert.Equal(1, service.CallCount);

        // The last message handed to the service is the question, and it comes after the system
        // instructions rather than instead of them.
        var last = service.LastMessages!.Last();
        Assert.Equal(AIMessageRole.User, last.Role);
        Assert.Equal("Hello", last.Content);
        Assert.Contains(service.LastMessages!, message => message.Role == AIMessageRole.System);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_WithEmptyMessage_ThrowsArgumentException(string message)
    {
        var service = new FakeAIService();
        var handler = CreateHandler(service);

        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(new SendMessageCommand(message)));

        Assert.Equal(0, service.CallCount);
    }

    [Fact]
    public async Task HandleAsync_RecordsTheQuestionAndTheAnswerInTheConversation()
    {
        var service = new FakeAIService { Response = AIResponse.Success("An answer", AIProviderType.Mock) };
        var handler = CreateHandler(service);
        var conversations = AITestHarness.CreateConversations();
        handler = new SendMessageHandler(
            service,
            AITestHarness.CreateContextBuilder(),
            conversations,
            NullLogger<SendMessageHandler>.Instance);

        var conversation = await conversations.CreateConversationAsync();
        await handler.HandleAsync(new SendMessageCommand("A question", conversation.Id));

        var stored = await conversations.GetConversationAsync(conversation.Id);
        Assert.NotNull(stored);
        var recorded = stored.Messages.ToArray();
        Assert.Equal(2, recorded.Length);
        Assert.Equal("A question", recorded[0].Content);
        Assert.Equal("An answer", recorded[1].Content);
    }

    [Fact]
    public async Task HandleAsync_WhenTheServiceFails_DoesNotRecordAnAnswer()
    {
        var service = new FakeAIService
        {
            Response = AIResponse.Failure("Not now.", AIProviderType.Mock, "AI_REQUEST_FAILED"),
        };
        var conversations = AITestHarness.CreateConversations();
        var handler = new SendMessageHandler(
            service,
            AITestHarness.CreateContextBuilder(),
            conversations,
            NullLogger<SendMessageHandler>.Instance);

        var conversation = await conversations.CreateConversationAsync();
        var response = await handler.HandleAsync(new SendMessageCommand("A question", conversation.Id));

        Assert.False(response.IsSuccessful);

        // Only the question is kept. An empty assistant turn would be shown as a blank reply.
        var stored = await conversations.GetConversationAsync(conversation.Id);
        Assert.NotNull(stored);
        Assert.Equal("A question", Assert.Single(stored.Messages).Content);
    }

    [Fact]
    public async Task HandleAsync_IncludesEarlierTurnsSoFollowUpQuestionsHaveContext()
    {
        var service = new FakeAIService();
        var conversations = AITestHarness.CreateConversations();
        var handler = new SendMessageHandler(
            service,
            AITestHarness.CreateContextBuilder(),
            conversations,
            NullLogger<SendMessageHandler>.Instance);

        var conversation = await conversations.CreateConversationAsync();
        await handler.HandleAsync(new SendMessageCommand("First question", conversation.Id));
        await handler.HandleAsync(new SendMessageCommand("Second question", conversation.Id));

        // The second request must carry the whole exchange, not just the latest question, or a
        // follow-up like "and the other one?" has nothing to refer to.
        var contents = service.LastMessages!.Select(message => message.Content).ToArray();
        Assert.Contains("First question", contents);
        Assert.Contains("Second question", contents);
    }
}
