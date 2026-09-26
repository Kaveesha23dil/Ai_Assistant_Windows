using WindowsAIAssistant.Core.Exceptions;

namespace WindowsAIAssistant.Application.Common.Exceptions;

public sealed class ConversationNotFoundException : AssistantException
{
    public ConversationNotFoundException(Guid conversationId)
        : base($"Conversation '{conversationId}' was not found.")
    {
        ConversationId = conversationId;
    }

    public Guid ConversationId { get; }
}
