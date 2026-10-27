using System.Threading;
using System.Threading.Tasks;
using WindowsAIAssistant.Core.Models.Clipboard;

namespace WindowsAIAssistant.Core.Abstractions.Clipboard;

public interface IClipboardHistoryService
{
    bool IsEnabled { get; set; }
    Task<ClipboardItem?> GetLastItemAsync(CancellationToken cancellationToken = default);
    Task SaveForUndoAsync(ClipboardContent content, CancellationToken cancellationToken = default);
    Task UndoLastReplacementAsync(CancellationToken cancellationToken = default);
}
