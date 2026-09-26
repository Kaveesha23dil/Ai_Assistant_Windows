namespace WindowsAIAssistant.Core.Models;

/// <summary>
/// A snapshot of the machine's battery state. <see cref="IsBatteryPresent"/> is false on
/// desktops, in which case the remaining values carry no meaning.
/// </summary>
public sealed record BatteryStatus
{
    public BatteryStatus(
        int percentage,
        bool isCharging,
        bool isBatteryPresent,
        TimeSpan? estimatedRemainingTime)
    {
        Percentage = Math.Clamp(percentage, 0, 100);
        IsCharging = isCharging;
        IsBatteryPresent = isBatteryPresent;
        EstimatedRemainingTime = estimatedRemainingTime is { } remaining && remaining > TimeSpan.Zero
            ? remaining
            : null;
    }

    /// <summary>Gets the remaining charge between 0 and 100.</summary>
    public int Percentage { get; }

    /// <summary>Gets a value indicating whether the machine is currently charging.</summary>
    public bool IsCharging { get; }

    /// <summary>Gets a value indicating whether the machine has a battery at all.</summary>
    public bool IsBatteryPresent { get; }

    /// <summary>Gets the estimated discharge time, when the platform reports one.</summary>
    public TimeSpan? EstimatedRemainingTime { get; }

    /// <summary>Creates a status describing a machine with no battery.</summary>
    public static BatteryStatus NotPresent() => new(0, isCharging: false, isBatteryPresent: false, estimatedRemainingTime: null);
}
