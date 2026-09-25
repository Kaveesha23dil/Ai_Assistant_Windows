using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Clipboard;
using WindowsAIAssistant.Core.Common;

namespace WindowsAIAssistant.Application.Clipboard.Commands.SetClipboardText;

public sealed class SetClipboardTextHandler
{
    private readonly IClipboardService _clipboardService;
    private readonly ILogger<SetClipboardTextHandler> _logger;

    public SetClipboardTextHandler(IClipboardService clipboardService, ILogger<SetClipboardTextHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(clipboardService);
        ArgumentNullException.ThrowIfNull(logger);
        _clipboardService = clipboardService;
        _logger = logger;
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

        _logger.LogInformation("Clipboard text update requested.");
        try
        {
            var result = await _clipboardService
                .SetTextAsync(command.Text, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (result.IsSuccess)
            {
                _logger.LogInformation("Clipboard text updated successfully.");
            }
            else
            {
                _logger.LogWarning("Clipboard text update failed. {Reason}", result.ErrorMessage);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Clipboard text update cancelled by caller.");
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Clipboard text update failed.");
            throw;
        }
    }
}
