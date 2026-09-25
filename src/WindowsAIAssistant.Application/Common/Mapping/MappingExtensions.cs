using WindowsAIAssistant.Application.DTOs;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Application.Common.Mapping;

public static class MappingExtensions
{
    public static AIMessageDto ToDto(this AIMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new AIMessageDto(
            message.Id,
            message.Role,
            message.Content,
            message.Timestamp);
    }

    public static ConversationDto ToDto(this AIConversation conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        return new ConversationDto(
            conversation.Id,
            conversation.Title,
            conversation.Messages.Select(message => message.ToDto()).ToArray(),
            conversation.CreatedAt,
            conversation.UpdatedAt);
    }

    public static FileSearchResultDto ToDto(this FileSearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new FileSearchResultDto(
            result.Name,
            result.FullPath,
            result.Extension,
            result.Size,
            result.LastModified,
            result.RelevanceScore);
    }

    public static SystemInformationDto ToDto(this SystemInformation information)
    {
        ArgumentNullException.ThrowIfNull(information);

        return new SystemInformationDto(
            information.MachineName,
            information.OperatingSystem,
            information.OperatingSystemVersion,
            information.UserName,
            information.ProcessorCount,
            information.TotalMemory,
            information.AvailableMemory);
    }
}
