using System.Diagnostics;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Computer;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Computer;

namespace WindowsAIAssistant.Infrastructure.Computer;

public sealed class WindowsComputerApplicationLauncher : IComputerApplicationLauncher
{
    private readonly IApplicationLauncherService _launcher;
    private readonly IWindowManager _windows;
    private readonly ILogger<WindowsComputerApplicationLauncher> _logger;

    public WindowsComputerApplicationLauncher(
        IApplicationLauncherService launcher,
        IWindowManager windows,
        ILogger<WindowsComputerApplicationLauncher> logger)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(logger);

        _launcher = launcher;
        _windows = windows;
        _logger = logger;
    }

    public async Task<Result<ApplicationLaunchResult>> LaunchAsync(
        string applicationName,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);

        var started = Stopwatch.StartNew();
        var launch = await _launcher.LaunchAsync(applicationName, cancellationToken).ConfigureAwait(false);
        if (launch.IsFailure)
        {
            return Result<ApplicationLaunchResult>.Failure(
                ErrorCodes.ComputerActionFailed,
                launch.ErrorMessage ?? $"I couldn't open {applicationName}.");
        }

        WindowInfo? window = null;
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var found = await _windows.FindWindowAsync(applicationName, cancellationToken)
                .ConfigureAwait(false);
            if (found.IsSuccess && found.Value is not null)
            {
                window = found.Value;
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(false);
        }

        started.Stop();

        _logger.LogInformation(
            "Computer control launched {ApplicationName} in {ElapsedMilliseconds} ms.",
            applicationName,
            started.ElapsedMilliseconds);

        return Result<ApplicationLaunchResult>.Success(new ApplicationLaunchResult
        {
            ApplicationName = applicationName,
            ProcessId = window?.ProcessId,
            WindowHandle = window?.Handle,
            Duration = started.Elapsed
        });
    }
}
