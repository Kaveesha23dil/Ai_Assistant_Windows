namespace WindowsAIAssistant.Core.Models;

/// <summary>A snapshot of the system output volume.</summary>
public sealed record VolumeStatus
{
    public VolumeStatus(int volume, bool isMuted)
    {
        Volume = Math.Clamp(volume, 0, 100);
        IsMuted = isMuted;
    }

    /// <summary>Gets the output volume percentage between 0 and 100.</summary>
    public int Volume { get; }

    /// <summary>Gets a value indicating whether output is muted.</summary>
    public bool IsMuted { get; }
}
