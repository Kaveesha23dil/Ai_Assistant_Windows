using System.Diagnostics;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Common;

namespace WindowsAIAssistant.Infrastructure.Windows;

/// <summary>
/// Launches a resolved application through the Windows shell.
/// <para>
/// The launcher accepts a human name and immediately resolves it against
/// <see cref="IApplicationResolver"/>. It never accepts a path or an argument list, so the
/// shell process is started with the target the resolver produced and nothing else; there is
/// no way for spoken text to become a command line or to carry arguments into another program.
/// </para>
/// </summary>
public sealed class WindowsApplicationLauncherService : IApplicationLauncherService
{
    private readonly IApplicationResolver _resolver;
    private readonly ILogger<WindowsApplicationLauncherService> _logger;

    public WindowsApplicationLauncherService(
        IApplicationResolver resolver,
        ILogger<WindowsApplicationLauncherService> logger)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(logger);

        _resolver = resolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result> LaunchAsync(
        string applicationName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(applicationName))
        {
            return Result.Failure("I didn't catch which application you meant.");
        }

        var resolved = await _resolver
            .ResolveAsync(applicationName, cancellationToken)
            .ConfigureAwait(false);

        if (resolved.IsFailure || resolved.Value is null)
        {
            return Result.Failure(resolved.ErrorMessage ?? "I couldn't find that application.");
        }

        var target = resolved.Value;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = target.Target,
                UseShellExecute = true
            };

            if (target.Kind == Core.Enums.ApplicationTargetKind.Uri)
            {
                startInfo.Verb = "open";
            }

            Process.Start(startInfo);

            _logger.LogInformation("Launched {ApplicationName}.", target.Name);
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Application launch was cancelled.");
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Launching a resolved application failed.");
            return Result.Failure($"I couldn't open {target.Name}.");
        }
    }
}
