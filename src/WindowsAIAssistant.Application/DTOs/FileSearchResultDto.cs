namespace WindowsAIAssistant.Application.DTOs;

public sealed record FileSearchResultDto(
    string Name,
    string FullPath,
    string Extension,
    long Size,
    DateTimeOffset LastModified,
    double RelevanceScore);
