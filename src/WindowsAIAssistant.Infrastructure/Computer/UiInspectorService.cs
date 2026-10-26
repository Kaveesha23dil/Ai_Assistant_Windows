using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Computer;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Computer;

namespace WindowsAIAssistant.Infrastructure.Computer;

public sealed class UiInspectorService : IUiInspectorService
{
    private readonly IUiElementFinder _finder;
    private readonly IWindowManager _windows;
    private readonly ILogger<UiInspectorService> _logger;

    public UiInspectorService(
        IUiElementFinder finder,
        IWindowManager windows,
        ILogger<UiInspectorService> logger)
    {
        ArgumentNullException.ThrowIfNull(finder);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(logger);

        _finder = finder;
        _windows = windows;
        _logger = logger;
    }

    public async Task<Result<UiInspection>> InspectAsync(
        UiElementQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var window = await ResolveWindowAsync(query, cancellationToken).ConfigureAwait(false);
        if (window.IsFailure)
        {
            return Result<UiInspection>.Failure(
                window.ErrorCode ?? ErrorCodes.ComputerTargetNotFound,
                window.ErrorMessage ?? "I couldn't find that window.");
        }

        var elements = await _finder.FindAllAsync(query, cancellationToken).ConfigureAwait(false);
        if (elements.IsFailure || elements.Value is null)
        {
            return Result<UiInspection>.Failure(
                elements.ErrorCode ?? ErrorCodes.ComputerActionFailed,
                elements.ErrorMessage ?? "I couldn't inspect that window.");
        }

        var focused = await _finder
            .GetFocusedElementAsync(window.Value?.Handle, cancellationToken)
            .ConfigureAwait(false);

        return Result<UiInspection>.Success(new UiInspection
        {
            Window = window.Value,
            FocusedElement = focused.IsSuccess ? focused.Value : null,
            Elements = elements.Value
        });
    }

    private async Task<Result<WindowInfo>> ResolveWindowAsync(
        UiElementQuery query,
        CancellationToken cancellationToken)
    {
        if (query.WindowHandle is { } handle && handle != 0)
        {
            var windows = await _windows.GetWindowsAsync(true, cancellationToken).ConfigureAwait(false);
            if (windows.IsSuccess && windows.Value is not null)
            {
                var match = windows.Value.FirstOrDefault(window => window.Handle == handle);
                if (match is not null)
                {
                    return Result<WindowInfo>.Success(match);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(query.WindowTitle)
            || !string.IsNullOrWhiteSpace(query.ProcessName))
        {
            return await _windows
                .FindWindowAsync(query.WindowTitle ?? query.ProcessName!, cancellationToken)
                .ConfigureAwait(false);
        }

        return await _windows.GetActiveWindowAsync(cancellationToken).ConfigureAwait(false);
    }
}
