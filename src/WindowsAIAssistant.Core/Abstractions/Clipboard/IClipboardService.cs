using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Clipboard;

namespace WindowsAIAssistant.Core.Abstractions.Clipboard;

public interface IClipboardService
{
    Task<string?> GetTextAsync(CancellationToken cancellationToken = default);
    Task<Result> SetTextAsync(string text, CancellationToken cancellationToken = default);

    async Task<ClipboardContent> GetCurrentContentAsync(CancellationToken cancellationToken = default)
    {
        var content = new ClipboardContent();
        var text = await GetTextAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(text))
        {
            content.Text = text;
            content.Type = ClipboardContentType.PlainText;
        }
        return content;
    }

    Task WriteTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Task.CompletedTask;
        }
        return SetTextAsync(text, cancellationToken).ContinueWith(_ => Task.CompletedTask, cancellationToken).Unwrap();
    }

    Task ClearAsync(CancellationToken cancellationToken = default)
    {
        return SetTextAsync(string.Empty, cancellationToken).ContinueWith(_ => Task.CompletedTask, cancellationToken).Unwrap();
    }

    List<string> GetAvailableFormats()
    {
        return new List<string> { "Text" };
    }
}
