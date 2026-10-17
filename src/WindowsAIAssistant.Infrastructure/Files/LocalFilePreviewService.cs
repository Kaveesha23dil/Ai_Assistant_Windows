using WindowsAIAssistant.Core.Abstractions.Documents;
using WindowsAIAssistant.Core.Abstractions.Files;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Infrastructure.Files;

public sealed class LocalFilePreviewService(IDocumentReader reader, IDocumentTypeDetector types, IPermissionService permissions) : IFilePreviewService
{
    public async Task<FilePreview> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!permissions.IsGranted(PermissionCapability.FileSearch))
            throw new UnauthorizedAccessException(permissions.GetDeniedMessage(PermissionCapability.FileSearch));
        return await Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = new FileInfo(path);
            if (!file.Exists) throw new FileNotFoundException("This file has moved or been deleted.");
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0) return new FilePreview(string.Empty, "Linked or cloud-placeholder files are not previewed. Open the file directly instead.");
            if (file.Length > 8 * 1024 * 1024) return new FilePreview(string.Empty, "Preview is limited to files up to 8 MB. Open the file or use the Documents page.");
            if (!types.IsSupported(path)) return new FilePreview(string.Empty, "No text preview is available for this file type. Use Open to view it in its default application.");
            var content = await reader.ReadAsync(path, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!permissions.IsGranted(PermissionCapability.FileSearch)) throw new UnauthorizedAccessException("File search permission was switched off.");
            var text = content.RawText;
            var notice = "Local text preview. No content was sent to AI.";
            if (text.Length > 12_000) { text = text[..12_000]; notice += " Showing the first 12,000 characters."; }
            if (content.IsEmpty) notice += " No readable text found; scanned documents may require OCR.";
            foreach (var warning in content.ExtractionWarnings) notice += " " + warning.Message;
            return new FilePreview(text, notice);
        }, cancellationToken).ConfigureAwait(false);
    }
}
