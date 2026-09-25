using WindowsAIAssistant.Core.Common;

namespace WindowsAIAssistant.Core.Abstractions.Clipboard;

/// <summary>
/// Reads and writes text from the system clipboard.
/// </summary>
public interface IClipboardService
{
    /// <summary>Gets the current clipboard text, when present.</summary>
    Task<string?> GetTextAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes text to the system clipboard.</summary>
    Task<Result> SetTextAsync(string text, CancellationToken cancellationToken = default);
}