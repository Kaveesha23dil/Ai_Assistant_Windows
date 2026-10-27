using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WindowsAIAssistant.Core.Abstractions.Clipboard;
using WindowsAIAssistant.Core.Models.Clipboard;

namespace WindowsAIAssistant.Infrastructure.Clipboard;

public sealed class ClipboardHistoryService : IClipboardHistoryService
{
    private ClipboardItem? _lastUndoItem;

    public bool IsEnabled { get; set; } = false;

    public Task<ClipboardItem?> GetLastItemAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return Task.FromResult<ClipboardItem?>(null);
        }
        return Task.FromResult(_lastUndoItem);
    }

    public Task SaveForUndoAsync(ClipboardContent content, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return Task.CompletedTask;
        }
        _lastUndoItem = new ClipboardItem
        {
            Content = new ClipboardContent
            {
                Text = content?.Text,
                Type = content?.Type ?? ClipboardContentType.Unknown,
                ImageData = content?.ImageData,
                FilePaths = new List<string>(content?.FilePaths ?? new List<string>())
            }
        };
        return Task.CompletedTask;
    }

    public Task UndoLastReplacementAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || _lastUndoItem == null)
        {
            return Task.CompletedTask;
        }
        _lastUndoItem = null;
        return Task.CompletedTask;
    }
}
