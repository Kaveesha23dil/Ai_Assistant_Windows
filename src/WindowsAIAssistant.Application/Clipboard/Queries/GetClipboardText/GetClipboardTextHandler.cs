using WindowsAIAssistant.Core.Abstractions.Clipboard;

namespace WindowsAIAssistant.Application.Clipboard.Queries.GetClipboardText;

public sealed class GetClipboardTextHandler
{
    private readonly IClipboardService _clipboardService;

    public GetClipboardTextHandler(IClipboardService clipboardService)
    {
        ArgumentNullException.ThrowIfNull(clipboardService);
        _clipboardService = clipboardService;
    }

    public async Task<string?> HandleAsync(
        GetClipboardTextQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        return await _clipboardService.GetTextAsync(cancellationToken).ConfigureAwait(false);
    }
}
