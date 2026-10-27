using System.Threading;
using System.Threading.Tasks;
using WindowsAIAssistant.Core.Models.Clipboard;

namespace WindowsAIAssistant.Core.Abstractions.Clipboard;

public interface IClipboardContentAnalyzer
{
    Task<ClipboardContent> AnalyzeAsync(ClipboardContent content, CancellationToken cancellationToken = default);
    ClipboardContentType DetectContentType(ClipboardContent content);
    double GetConfidence(ClipboardContent content, ClipboardContentType type);
}
