using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Application.Common.Mapping;
using WindowsAIAssistant.Application.DTOs;
using WindowsAIAssistant.Core.Abstractions.System;

namespace WindowsAIAssistant.Application.System.Queries.GetSystemInformation;

public sealed class GetSystemInformationHandler
{
    private readonly IWindowsSystemService _windowsSystemService;
    private readonly ILogger<GetSystemInformationHandler> _logger;

    public GetSystemInformationHandler(
        IWindowsSystemService windowsSystemService,
        ILogger<GetSystemInformationHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(windowsSystemService);
        ArgumentNullException.ThrowIfNull(logger);
        _windowsSystemService = windowsSystemService;
        _logger = logger;
    }

    public async Task<SystemInformationDto> HandleAsync(
        GetSystemInformationQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation("System information request started.");
        try
        {
            var information = await _windowsSystemService
                .GetSystemInformationAsync(cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation("System information request completed.");
            return information.ToDto();
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("System information request cancelled by caller.");
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "System information request failed.");
            throw;
        }
    }
}
