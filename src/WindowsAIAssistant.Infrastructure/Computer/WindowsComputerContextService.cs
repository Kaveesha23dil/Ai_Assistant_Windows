using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.Computer;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Computer;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.Computer;

public sealed class WindowsComputerContextService : IComputerContextService
{
    private readonly IWindowManager _windows;
    private readonly IOptionsMonitor<ComputerControlOptions> _options;
    private readonly ILogger<WindowsComputerContextService> _logger;

    public WindowsComputerContextService(
        IWindowManager windows,
        IOptionsMonitor<ComputerControlOptions> options,
        ILogger<WindowsComputerContextService> logger)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _windows = windows;
        _options = options;
        _logger = logger;
    }

    public async Task<Result<ComputerContext>> GetContextAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var active = await _windows.GetActiveWindowAsync(cancellationToken).ConfigureAwait(false);
            var windows = await _windows.GetWindowsAsync(false, cancellationToken).ConfigureAwait(false);
            var monitors = await _windows.GetMonitorsAsync(cancellationToken).ConfigureAwait(false);
            var cursor = await _windows.GetCursorPositionAsync(cancellationToken).ConfigureAwait(false);

            if (windows.IsFailure || windows.Value is null)
            {
                return Result<ComputerContext>.Failure(
                    windows.ErrorCode ?? ErrorCodes.ComputerActionFailed,
                    windows.ErrorMessage ?? "I couldn't read the open windows.");
            }

            return Result<ComputerContext>.Success(new ComputerContext
            {
                ActiveWindow = active.IsSuccess ? active.Value : null,
                Windows = windows.Value,
                Monitors = monitors.IsSuccess && monitors.Value is not null ? monitors.Value : [],
                CursorX = cursor.IsSuccess && cursor.Value is { } point ? point.X : 0,
                CursorY = cursor.IsSuccess && cursor.Value is { } point2 ? point2.Y : 0,
                IsSafeMode = _options.CurrentValue.SafeMode
            });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Reading the computer context failed.");
            return Result<ComputerContext>.Failure(
                ErrorCodes.ComputerActionFailed,
                "I couldn't read the current window and display information.");
        }
    }
}
