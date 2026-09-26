using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Exceptions;

namespace WindowsAIAssistant.Infrastructure.Windows;

/// <summary>
/// Hands a URI to the Windows shell so it opens in whatever application the user has chosen.
/// <para>
/// This is the only route by which the assistant opens a web page, a settings page, or a
/// folder. The caller supplies a <see cref="Uri"/> it built itself; a transcript is never
/// concatenated into a command line or a shell string, so a spoken sentence cannot smuggle
/// in an argument.
/// </para>
/// </summary>
public sealed class UriLauncherService : IUriLauncherService
{
    private readonly ILogger<UriLauncherService> _logger;

    public UriLauncherService(ILogger<UriLauncherService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result> OpenAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri || !uri.IsWellFormedOriginalString())
        {
            return Result.Failure("That address could not be opened.");
        }

        try
        {
            var launched = await global::Windows.System.Launcher
                .LaunchUriAsync(uri)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            if (launched)
            {
                return Result.Success();
            }

            _logger.LogInformation(
                "The shell declined to open a {Scheme} address; no application is registered for it.",
                uri.Scheme);

            return Result.Failure("No application is registered to open that.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Opening a shell address was cancelled.");
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Opening a shell address failed.");
            throw new WindowsServiceException("The shell could not open that address.", exception);
        }
    }
}
