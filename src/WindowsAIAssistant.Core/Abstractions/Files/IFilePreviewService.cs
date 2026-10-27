using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Core.Abstractions.Files;

public interface IFilePreviewService
{
    Task<FilePreview> ReadAsync(string path, CancellationToken cancellationToken = default);
}
