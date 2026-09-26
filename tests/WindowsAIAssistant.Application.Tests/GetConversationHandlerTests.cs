using Microsoft.Extensions.Logging.Abstractions;
using WindowsAIAssistant.Application.AI.Queries.GetConversation;
using WindowsAIAssistant.Application.AI.Services;

namespace WindowsAIAssistant.Application.Tests;

public sealed class GetConversationHandlerTests
{
    [Fact]
    public async Task HandleAsync_WithExistingConversation_ReturnsConversation()
    {
        var service = new ConversationService(NullLogger<ConversationService>.Instance);
        var conversation = await service.CreateConversationAsync();
        var handler = new GetConversationHandler(service);

        var result = await handler.HandleAsync(new GetConversationQuery(conversation.Id));

        Assert.NotNull(result);
        Assert.Equal(conversation.Id, result.Id);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownConversation_ReturnsNull()
    {
        var handler = new GetConversationHandler(
            new ConversationService(NullLogger<ConversationService>.Instance));

        var result = await handler.HandleAsync(new GetConversationQuery(Guid.NewGuid()));

        Assert.Null(result);
    }
}
