namespace WindowsAIAssistant.Application.AI.Commands.SendMessage;

/// <param name="Message">What the person asked. Must not be empty.</param>
/// <param name="ConversationId">
/// The conversation to answer within, or <see langword="null"/> to start one. Supplying it is
/// what gives the assistant the earlier turns; leaving it out is a single question with no
/// memory of the last one.
/// </param>
public sealed record SendMessageCommand(string Message, Guid? ConversationId = null);
