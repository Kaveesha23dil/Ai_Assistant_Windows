using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Enums;

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
    private readonly IPermissionService _permissions;

    public UriLauncherService(ILogger<UriLauncherService> logger, IPermissionService permissions)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _permissions = permissions;
    }

    /// <inheritdoc />
    public async Task<Result> OpenAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri || (!uri.IsFile && !uri.IsWellFormedOriginalString()))
        {
            return Result.Failure("That address could not be opened.");
        }

        try
        {
            if (uri.IsFile)
            {
                foreach (var capability in new[] { PermissionCapability.FileSearch, PermissionCapability.ApplicationLaunch })
                    if (!_permissions.IsGranted(capability)) return Result.Failure(_permissions.GetDeniedMessage(capability));
                var path = uri.LocalPath;
                if (Directory.Exists(path))
                {
                    var folder = await global::Windows.Storage.StorageFolder.GetFolderFromPathAsync(path).AsTask(cancellationToken);
                    return await global::Windows.System.Launcher.LaunchFolderAsync(folder).AsTask(cancellationToken)
                        ? Result.Success() : Result.Failure("Windows could not open this folder.");
                }
                if (!File.Exists(path)) return Result.Failure("This file has moved or been deleted.");
                var file = await global::Windows.Storage.StorageFile.GetFileFromPathAsync(path).AsTask(cancellationToken);
                return await global::Windows.System.Launcher.LaunchFileAsync(file).AsTask(cancellationToken)
                    ? Result.Success() : Result.Failure("No application is registered to open this file.");
            }
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
