using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Core.Abstractions.System;

/// <summary>
/// Reads and changes the system output volume.
/// <para>
/// Implementations expose only level and mute. There is deliberately no "run this audio
/// command" member, which is what keeps the voice assistant away from arbitrary shell
/// execution.
/// </para>
/// </summary>
public interface IVolumeService
{
    /// <summary>Reads the current output volume and mute state.</summary>
    Task<Result<VolumeStatus>> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Sets the output volume. Values outside 0 to 100 are rejected.</summary>
    Task<Result<VolumeStatus>> SetVolumeAsync(int volume, CancellationToken cancellationToken = default);

    /// <summary>Raises the output volume by the platform's own step size.</summary>
    Task<Result<VolumeStatus>> IncreaseAsync(CancellationToken cancellationToken = default);

    /// <summary>Lowers the output volume by the platform's own step size.</summary>
    Task<Result<VolumeStatus>> DecreaseAsync(CancellationToken cancellationToken = default);

    /// <summary>Mutes or unmutes output.</summary>
    Task<Result<VolumeStatus>> SetMutedAsync(bool isMuted, CancellationToken cancellationToken = default);
}
