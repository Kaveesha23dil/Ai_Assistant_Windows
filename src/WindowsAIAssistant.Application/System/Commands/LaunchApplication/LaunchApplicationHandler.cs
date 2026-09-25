using WindowsAIAssistant.Application.Common.Validation;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Common;

namespace WindowsAIAssistant.Application.System.Commands.LaunchApplication;

public sealed class LaunchApplicationHandler
{
    private readonly IApplicationLauncherService _applicationLauncherService;

    public LaunchApplicationHandler(IApplicationLauncherService applicationLauncherService)
    {
        ArgumentNullException.ThrowIfNull(applicationLauncherService);
        _applicationLauncherService = applicationLauncherService;
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

        return await _applicationLauncherService
            .LaunchAsync(applicationName, cancellationToken)
            .ConfigureAwait(false);
    }
}
