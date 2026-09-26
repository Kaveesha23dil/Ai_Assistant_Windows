using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Infrastructure.Windows;

/// <summary>
/// Reports battery charge and charging state through the platform power API.
/// <para>
/// The platform types are named in full rather than imported, because the Windows namespaces
/// declare their own <c>BatteryStatus</c> and the application's model of a battery is a
/// different thing entirely.
/// </para>
/// <para>
/// The power API reports no percentage of its own on this platform, so the figure is
/// derived from the battery's reported capacities. A desktop has no aggregate battery at
/// all, and answering "0 percent" there would be a number the user cannot act on, so this
/// reports <see cref="Core.Models.BatteryStatus.NotPresent"/> instead.
/// </para>
/// </summary>
public sealed class BatteryService : IBatteryService
{
    private readonly ILogger<BatteryService> _logger;

    public BatteryService(ILogger<BatteryService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsBatteryPresent
    {
        get
        {
            try
            {
                return global::Windows.System.Power.PowerManager.BatteryStatus
                    != global::Windows.System.Power.BatteryStatus.NotPresent;
            }
            catch (Exception exception)
            {
                _logger.LogDebug(
                    "Reading the battery presence flag failed (HRESULT 0x{Result:X8}).",
                    exception.HResult);
                return false;
            }
        }
    }

    /// <inheritdoc />
    public Task<Result<BatteryStatus>> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (!IsBatteryPresent)
            {
                return Task.FromResult(Result<BatteryStatus>.Success(BatteryStatus.NotPresent()));
            }

            var report = global::Windows.Devices.Power.Battery.AggregateBattery.GetReport();
            var isCharging =
                report.Status == global::Windows.System.Power.BatteryStatus.Charging;

            return Task.FromResult(Result<BatteryStatus>.Success(
                new BatteryStatus(
                    ToPercentage(report),
                    isCharging,
                    isBatteryPresent: true,
                    estimatedRemainingTime: null)));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Reading battery status failed (HRESULT 0x{Result:X8}).",
                exception.HResult);
            return Task.FromResult(
                Result<BatteryStatus>.Failure("I couldn't read your battery status."));
        }
    }

    /// <summary>
    /// Derives a charge percentage from the reported capacities, preferring the full charge
    /// capacity over the design capacity because that is what the battery actually holds now.
    /// Returns zero when the battery declines to report either figure.
    /// </summary>
    private static int ToPercentage(global::Windows.Devices.Power.BatteryReport report)
    {
        var remaining = report.RemainingCapacityInMilliwattHours;
        if (remaining is null or <= 0)
        {
            return 0;
        }

        var full = report.FullChargeCapacityInMilliwattHours is > 0
            ? report.FullChargeCapacityInMilliwattHours
            : report.DesignCapacityInMilliwattHours;

        if (full is null or <= 0)
        {
            return 0;
        }

        var percentage = (int)Math.Round(
            remaining.Value * 100d / full.Value,
            MidpointRounding.AwayFromZero);

        return Math.Clamp(percentage, 0, 100);
    }
}
