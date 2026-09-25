using WindowsAIAssistant.Application.AI.Services;
using WindowsAIAssistant.Application.DTOs;

namespace WindowsAIAssistant.Application.AI.Queries.GetConversation;

public sealed class GetConversationHandler
{
    private readonly IConversationService _conversationService;

    public GetConversationHandler(IConversationService conversationService)
    {
        ArgumentNullException.ThrowIfNull(conversationService);
        _conversationService = conversationService;
    }

    public async Task<ConversationDto?> HandleAsync(
        GetConversationQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (query.ConversationId == Guid.Empty)
        {
            throw new ArgumentException("Conversation id cannot be empty.", nameof(query.ConversationId));
        }

        return await _conversationService
            .GetConversationAsync(query.ConversationId, cancellationToken)
            .ConfigureAwait(false);
    }
}
