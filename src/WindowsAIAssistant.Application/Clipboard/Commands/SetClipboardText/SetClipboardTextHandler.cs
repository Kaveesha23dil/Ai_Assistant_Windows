using WindowsAIAssistant.Core.Abstractions.Clipboard;
using WindowsAIAssistant.Core.Common;

namespace WindowsAIAssistant.Application.Clipboard.Commands.SetClipboardText;

public sealed class SetClipboardTextHandler
{
    private readonly IClipboardService _clipboardService;

    public SetClipboardTextHandler(IClipboardService clipboardService)
    {
        ArgumentNullException.ThrowIfNull(clipboardService);
        _clipboardService = clipboardService;
    }

    public async Task<Result> HandleAsync(
        SetClipboardTextCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (command.Text is null)
        {
            throw new ArgumentNullException(nameof(command.Text), "Clipboard text cannot be null.");
        }

        return await _clipboardService
            .SetTextAsync(command.Text, cancellationToken)
            .ConfigureAwait(false);
    }
}
