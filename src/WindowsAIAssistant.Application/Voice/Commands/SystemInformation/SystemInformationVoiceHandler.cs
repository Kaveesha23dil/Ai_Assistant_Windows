using System.Globalization;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Application.System.Queries.GetSystemInformation;
using WindowsAIAssistant.Application.Voice.Text;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Voice.Commands.SystemInformation;

/// <summary>
/// Answers the read-only questions about the machine: what it is called, how much memory it
/// has, and how much disk space is left.
/// <para>
/// These three intents share a handler because they all read the same informational service.
/// A spoken answer is assembled from a single read rather than by formatting the full system
/// summary, so a user asking about memory does not hear their user name.
/// </para>
/// </summary>
public sealed class SystemInformationVoiceHandler : VoiceHandlerBase
{
    private static readonly AssistantIntent[] Supported =
    [
        AssistantIntent.GetSystemInformation,
        AssistantIntent.GetMemoryUsage,
        AssistantIntent.GetStorageUsage
    ];

    private readonly GetSystemInformationHandler _systemInformation;
    private readonly IDriveSpaceService _driveSpace;

    public SystemInformationVoiceHandler(
        GetSystemInformationHandler systemInformation,
        IDriveSpaceService driveSpace,
        IPermissionService permissions,
        ILogger<SystemInformationVoiceHandler> logger)
        : base(permissions, logger)
    {
        ArgumentNullException.ThrowIfNull(systemInformation);
        ArgumentNullException.ThrowIfNull(driveSpace);

        _systemInformation = systemInformation;
        _driveSpace = driveSpace;
    }

    /// <inheritdoc />
    public override IReadOnlyCollection<AssistantIntent> Intents => Supported;

    /// <inheritdoc />
    public override async Task<VoiceCommandResult> ExecuteAsync(
        VoiceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return command.Intent switch
        {
            AssistantIntent.GetStorageUsage => await ReportStorageAsync(command, cancellationToken)
                .ConfigureAwait(false),
            _ => await ReportSystemAsync(command, cancellationToken).ConfigureAwait(false)
        };
    }

    private async Task<VoiceCommandResult> ReportSystemAsync(
        VoiceCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var information = await _systemInformation
                .HandleAsync(new GetSystemInformationQuery(), cancellationToken)
                .ConfigureAwait(false);

            if (command.Intent == AssistantIntent.GetMemoryUsage)
            {
                var used = information.TotalMemory - information.AvailableMemory;
                var response =
                    $"You're using {ResponseTextFormatter.FormatBytes(used)} of " +
                    $"{ResponseTextFormatter.FormatBytes(information.TotalMemory)}.";

                return VoiceCommandResult.Success(
                    command,
                    response,
                    UsageData(used, information.TotalMemory));
            }

            var summary =
                $"This is {information.MachineName}, running {information.OperatingSystem} " +
                $"version {information.OperatingSystemVersion}, with " +
                $"{ResponseTextFormatter.FormatBytes(information.TotalMemory)} of memory and " +
                $"{information.ProcessorCount} processors.";

            return VoiceCommandResult.Success(command, summary);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Failed(command, exception);
        }
    }

    private async Task<VoiceCommandResult> ReportStorageAsync(
        VoiceCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var space = await _driveSpace.GetSystemDriveSpaceAsync(cancellationToken).ConfigureAwait(false);
            if (space.IsFailure || space.Value is null)
            {
                return Unavailable(command, "I couldn't read your disk space.");
            }

            var drive = space.Value;
            return VoiceCommandResult.Success(
                command,
                $"You have {ResponseTextFormatter.FormatBytes(drive.AvailableBytes)} free on {drive.DriveName}.",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["drive"] = drive.DriveName,
                    ["availableBytes"] = drive.AvailableBytes.ToString(CultureInfo.InvariantCulture),
                    ["totalBytes"] = drive.TotalBytes.ToString(CultureInfo.InvariantCulture)
                });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Failed(command, exception);
        }
    }

    private static Dictionary<string, string> UsageData(long usedBytes, long totalBytes) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["usedBytes"] = usedBytes.ToString(CultureInfo.InvariantCulture),
            ["totalBytes"] = totalBytes.ToString(CultureInfo.InvariantCulture)
        };
}
