using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Application.DTOs;

public sealed record AIMessageDto(
    Guid Id,
    AIMessageRole Role,
    string Content,
    DateTimeOffset Timestamp);
