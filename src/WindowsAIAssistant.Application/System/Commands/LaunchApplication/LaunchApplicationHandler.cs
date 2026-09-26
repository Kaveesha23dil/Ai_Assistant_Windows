using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Application.Common.Validation;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Common;

namespace WindowsAIAssistant.Application.System.Commands.LaunchApplication;

public sealed class LaunchApplicationHandler
{
    private readonly IApplicationLauncherService _applicationLauncherService;
    private readonly ILogger<LaunchApplicationHandler> _logger;

    public LaunchApplicationHandler(
        IApplicationLauncherService applicationLauncherService,
        ILogger<LaunchApplicationHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(applicationLauncherService);
        ArgumentNullException.ThrowIfNull(logger);
        _applicationLauncherService = applicationLauncherService;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(
        LaunchApplicationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var applicationName = ValidationHelper.RequireText(
            command.ApplicationName,
            nameof(command.ApplicationName),
            "Application name cannot be empty.");

        _logger.LogInformation("Application launch requested for {ApplicationName}.", applicationName);
        try
        {
            var result = await _applicationLauncherService
                .LaunchAsync(applicationName, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (result.IsSuccess)
            {
                _logger.LogInformation("Application launch succeeded for {ApplicationName}.", applicationName);
            }
            else
            {
                _logger.LogWarning(
                    "Application launch failed for {ApplicationName}. {Reason}",
                    applicationName,
                    result.ErrorMessage);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Application launch cancelled by caller.");
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Application launch failed.");
            throw;
        }
    }
}
