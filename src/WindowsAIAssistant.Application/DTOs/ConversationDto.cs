namespace WindowsAIAssistant.Application.DTOs;

public sealed record ConversationDto(
    Guid Id,
    string Title,
    IReadOnlyCollection<AIMessageDto> Messages,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
