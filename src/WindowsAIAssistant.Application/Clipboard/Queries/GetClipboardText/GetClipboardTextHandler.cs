using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Clipboard;

namespace WindowsAIAssistant.Application.Clipboard.Queries.GetClipboardText;

public sealed class GetClipboardTextHandler
{
    private readonly IClipboardService _clipboardService;
    private readonly ILogger<GetClipboardTextHandler> _logger;

    public GetClipboardTextHandler(IClipboardService clipboardService, ILogger<GetClipboardTextHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(clipboardService);
        ArgumentNullException.ThrowIfNull(logger);
        _clipboardService = clipboardService;
        _logger = logger;
    }

    public async Task<string?> HandleAsync(
        GetClipboardTextQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogDebug("Clipboard text retrieval started.");
        try
        {
            var text = await _clipboardService.GetTextAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation("Clipboard text retrieved successfully.");
            return text;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Clipboard text retrieval cancelled by caller.");
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Clipboard text retrieval failed.");
            throw;
        }
    }
}
