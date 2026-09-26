using System.Globalization;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Application.Voice.Text;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Voice.Commands.SystemInformation;

/// <summary>
/// Answers "how much battery is left".
/// <para>
/// A desktop reports no battery, and saying "0 percent" there would be a lie the user cannot
/// act on, so the handler says so plainly instead of answering with a number.
/// </para>
/// </summary>
public sealed class BatteryVoiceHandler : VoiceHandlerBase
{
    private static readonly AssistantIntent[] Supported = [AssistantIntent.GetBatteryStatus];

    private readonly IBatteryService _battery;

    public BatteryVoiceHandler(
        IBatteryService battery,
        IPermissionService permissions,
        ILogger<BatteryVoiceHandler> logger)
        : base(permissions, logger)
    {
        ArgumentNullException.ThrowIfNull(battery);

        _battery = battery;
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

        try
        {
            var status = await _battery.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            if (status.IsFailure || status.Value is null)
            {
                return Unavailable(command, "I couldn't read your battery status.");
            }

            if (!status.Value.IsBatteryPresent)
            {
                return VoiceCommandResult.Success(command, "This machine doesn't have a battery.");
            }

            var battery = status.Value;
            var response = battery.IsCharging
                ? $"Your battery is at {battery.Percentage} percent and charging."
                : $"Your battery is at {battery.Percentage} percent.";

            if (battery.EstimatedRemainingTime is { } remaining)
            {
                response += $" About {ResponseTextFormatter.FormatDuration(remaining)} left.";
            }

            return VoiceCommandResult.Success(
                command,
                response,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["percentage"] = battery.Percentage.ToString(CultureInfo.InvariantCulture),
                    ["isCharging"] = battery.IsCharging.ToString()
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
}
