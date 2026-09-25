using WindowsAIAssistant.Application.DTOs;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Application.AI.Services;

public interface IConversationService
{
    Task<ConversationDto> CreateConversationAsync(CancellationToken cancellationToken = default);

    Task AddMessageAsync(
        Guid conversationId,
        AIMessage message,
        CancellationToken cancellationToken = default);

    Task<ConversationDto?> GetConversationAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);

    Task ClearConversationAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);
}
