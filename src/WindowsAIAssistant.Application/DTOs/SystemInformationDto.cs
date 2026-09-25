namespace WindowsAIAssistant.Application.DTOs;

public sealed record SystemInformationDto(
    string MachineName,
    string OperatingSystem,
    string OperatingSystemVersion,
    string UserName,
    int ProcessorCount,
    long TotalMemory,
    long AvailableMemory);
