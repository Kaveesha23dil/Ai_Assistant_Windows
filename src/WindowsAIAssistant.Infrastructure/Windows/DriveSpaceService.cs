using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Infrastructure.Windows;

/// <summary>
/// Reports free and total space for the drive that holds Windows.
/// <para>
/// <see cref="DriveInfo"/> is a framework call, so this is a local read with no elevation and
/// no management instrumentation query.
/// </para>
/// </summary>
public sealed class DriveSpaceService : IDriveSpaceService
{
    private readonly ILogger<DriveSpaceService> _logger;

    public DriveSpaceService(ILogger<DriveSpaceService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
    }

    /// <inheritdoc />
    public Task<Result<DriveSpace>> GetSystemDriveSpaceAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            if (string.IsNullOrWhiteSpace(root))
            {
                return Task.FromResult(Result<DriveSpace>.Failure("I couldn't work out which drive Windows is on."));
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                return Task.FromResult(Result<DriveSpace>.Failure("I couldn't read that drive."));
            }

            return Task.FromResult(Result<DriveSpace>.Success(new DriveSpace(
                drive.Name,
                drive.TotalSize,
                drive.AvailableFreeSpace)));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Reading system drive space failed.");
            return Task.FromResult(Result<DriveSpace>.Failure("I couldn't read your disk space."));
        }
    }
}
