using Microsoft.Extensions.Logging.Abstractions;
using WindowsAIAssistant.Application.AI.Services;
using WindowsAIAssistant.Application.Common.Exceptions;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Application.Tests;

public sealed class ConversationServiceTests
{
    private static ConversationService CreateService() =>
        new(NullLogger<ConversationService>.Instance);

    [Fact]
    public async Task Conversation_CanStoreAndRetrieveMessages()
    {
        var service = CreateService();
        var conversation = await service.CreateConversationAsync();
        var message = new AIMessage(
            Guid.NewGuid(),
            AIMessageRole.User,
            "Hello",
            DateTimeOffset.UtcNow);

        await service.AddMessageAsync(conversation.Id, message);
        var result = await service.GetConversationAsync(conversation.Id);

        Assert.NotNull(result);
        var storedMessage = Assert.Single(result.Messages);
        Assert.Equal(message.Id, storedMessage.Id);
        Assert.Equal(message.Content, storedMessage.Content);
    }

    [Fact]
    public async Task ClearConversationAsync_RemovesStoredMessages()
    {
        var service = CreateService();
        var conversation = await service.CreateConversationAsync();
        await service.AddMessageAsync(conversation.Id, AIMessage.CreateAssistant("Hello"));

        await service.ClearConversationAsync(conversation.Id);
        var result = await service.GetConversationAsync(conversation.Id);

        Assert.NotNull(result);
        Assert.Empty(result.Messages);
    }

    [Fact]
    public async Task GetConversationAsync_WithUnknownId_ReturnsNull()
    {
        var service = CreateService();

        var result = await service.GetConversationAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task AddMessageAsync_WithUnknownConversation_Throws()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ConversationNotFoundException>(() => service.AddMessageAsync(
            Guid.NewGuid(),
            AIMessage.CreateUser("Hello")));
    }
}
