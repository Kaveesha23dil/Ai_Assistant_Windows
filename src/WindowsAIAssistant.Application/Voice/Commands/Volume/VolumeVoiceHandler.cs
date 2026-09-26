using System.Globalization;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Application.Voice.Text;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Voice.Commands.Volume;

/// <summary>
/// Handles every volume and mute request: raise it, lower it, set a percentage, mute, unmute.
/// <para>
/// Changing system audio is a side effect the user notices immediately, so the whole family
/// sits behind the system-control consent switch. The service interface exposes only level and
/// mute, which is what keeps speech from ever reaching an audio driver command line.
/// </para>
/// </summary>
public sealed class VolumeVoiceHandler : VoiceHandlerBase
{
    private const int MaximumVolume = 100;

    private static readonly AssistantIntent[] Supported =
    [
        AssistantIntent.IncreaseVolume,
        AssistantIntent.DecreaseVolume,
        AssistantIntent.SetVolume,
        AssistantIntent.MuteVolume,
        AssistantIntent.UnmuteVolume
    ];

    private readonly IVolumeService _volume;

    public VolumeVoiceHandler(
        IVolumeService volume,
        IPermissionService permissions,
        ILogger<VolumeVoiceHandler> logger)
        : base(permissions, logger)
    {
        ArgumentNullException.ThrowIfNull(volume);

        _volume = volume;
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

        if (!Permissions.IsGranted(PermissionCapability.SystemControl))
        {
            return Denied(command, PermissionCapability.SystemControl);
        }

        if (command.Intent == AssistantIntent.SetVolume
            && (!command.TryGetInt32(VoiceCommand.VolumeParameter, out var requested)
                || requested is < 0 or > MaximumVolume))
        {
            return Invalid(command, "I didn't catch which volume you wanted.");
        }

        try
        {
            var status = await ApplyAsync(command, cancellationToken).ConfigureAwait(false);
            if (status.IsFailure || status.Value is null)
            {
                return Unavailable(command, "I couldn't change the volume.");
            }

            return Describe(command, status.Value);
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

    private Task<Result<VolumeStatus>> ApplyAsync(VoiceCommand command, CancellationToken cancellationToken) =>
        command.Intent switch
        {
            AssistantIntent.IncreaseVolume => _volume.IncreaseAsync(cancellationToken),
            AssistantIntent.DecreaseVolume => _volume.DecreaseAsync(cancellationToken),
            AssistantIntent.SetVolume => _volume.SetVolumeAsync(
                command.GetParameter(VoiceCommand.VolumeParameter) is { } raw
                && int.TryParse(raw, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var value)
                    ? value
                    : 0,
                cancellationToken),
            AssistantIntent.MuteVolume => _volume.SetMutedAsync(true, cancellationToken),
            AssistantIntent.UnmuteVolume => _volume.SetMutedAsync(false, cancellationToken),
            _ => Task.FromResult(Result<VolumeStatus>.Failure("Unsupported volume command."))
        };

    private static VoiceCommandResult Describe(VoiceCommand command, VolumeStatus status)
    {
        var percentage = ResponseTextFormatter.FormatPercentage(status.Volume);

        var response = status.IsMuted
            ? "Audio is muted."
            : $"Volume is at {percentage} percent.";

        return VoiceCommandResult.Success(
            command,
            response,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["volume"] = status.Volume.ToString(CultureInfo.InvariantCulture),
                ["isMuted"] = status.IsMuted.ToString()
            });
    }
}
