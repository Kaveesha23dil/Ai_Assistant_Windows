namespace WindowsAIAssistant.Core.Models;

/// <summary>Free and total space for one drive.</summary>
/// <param name="DriveName">A display name such as "C:".</param>
/// <param name="TotalBytes">The drive's total capacity in bytes.</param>
/// <param name="AvailableBytes">The space a user can still write, in bytes.</param>
public sealed record DriveSpace(string DriveName, long TotalBytes, long AvailableBytes)
{
    /// <summary>Gets the share of the drive that is already in use, between 0 and 1.</summary>
    public double UsedFraction => TotalBytes <= 0
        ? 0.0
        : Math.Clamp(1.0 - (double)AvailableBytes / TotalBytes, 0.0, 1.0);
}
