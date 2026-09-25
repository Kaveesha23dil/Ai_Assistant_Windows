namespace WindowsAIAssistant.Application.Common.Exceptions;

public sealed class ConversationNotFoundException : KeyNotFoundException
{
    public ConversationNotFoundException(Guid conversationId)
        : base($"Conversation '{conversationId}' was not found.")
    {
        ConversationId = conversationId;
    }

    public Guid ConversationId { get; }
}
