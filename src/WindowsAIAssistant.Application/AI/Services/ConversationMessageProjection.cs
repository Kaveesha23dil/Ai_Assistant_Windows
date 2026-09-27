using WindowsAIAssistant.Application.DTOs;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Application.AI.Services;

/// <summary>
/// Projects a stored conversation back into the messages a provider is given.
/// <para>
/// The transcript is kept as a read model so the interface can be handed a shape that cannot
/// change underneath it, and this is the one place that shape is turned back into the domain
/// message a request is built from.
/// </para>
/// <para>
/// Only the turns a person and the assistant actually exchanged are carried over. A system
/// instruction is not part of the transcript: the context builder puts the current one at the
/// front of every request, so keeping an old copy would leave the provider holding two sets of
/// instructions, one of them stale.
/// </para>
/// </summary>
public static class ConversationMessageProjection
{
    /// <summary>
    /// Returns the user and assistant turns of a conversation, oldest first, or an empty
    /// collection when there is no conversation.
    /// </summary>
    public static IReadOnlyCollection<AIMessage> ToRequestMessages(this ConversationDto? conversation)
    {
        if (conversation is null)
        {
            return [];
        }

        return conversation.Messages
            .Where(message => message.Role is AIMessageRole.User or AIMessageRole.Assistant)
            .Select(message => new AIMessage(message.Id, message.Role, message.Content, message.Timestamp))
            .ToArray();
    }
}
