using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Infrastructure.Windows;

/// <summary>
/// Reads and changes the default output device's volume and mute state.
/// <para>
/// The implementation talks to the audio endpoint volume API directly. There is deliberately
/// no member that accepts a command, a script, or an argument string, and the audio session
/// is opened and closed locally with no process ever being started. That is what keeps a
/// spoken "volume up" from being a general-purpose remote command facility.
/// </para>
/// </summary>
public sealed class VolumeService : IVolumeService
{
    /// <summary>The platform's own step for the volume-up and volume-down keys.</summary>
    private const float StepPercentage = 5f;

    private const float MaximumVolume = 100f;

    private readonly ILogger<VolumeService> _logger;

    public VolumeService(ILogger<VolumeService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
    }

    /// <inheritdoc />
    public Task<Result<VolumeStatus>> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return Task.FromResult(Read());
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                "Reading the output volume failed (HRESULT 0x{Result:X8}).",
                exception.HResult);
            return Task.FromResult(Result<VolumeStatus>.Failure("I couldn't read your volume."));
        }
    }

    /// <inheritdoc />
    public Task<Result<VolumeStatus>> SetVolumeAsync(
        int volume,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (volume is < 0 or > 100)
        {
            return Task.FromResult(Result<VolumeStatus>.Failure("The volume must be between 0 and 100."));
        }

        return Apply(
            endpoint => endpoint.MasterVolumeLevel = volume,
            "I couldn't change the volume.");    }

    /// <inheritdoc />
    public Task<Result<VolumeStatus>> IncreaseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Apply(
            endpoint => endpoint.MasterVolumeLevel =
                Math.Min(MaximumVolume, endpoint.MasterVolumeLevel + StepPercentage),
            "I couldn't turn the volume up.");
    }

    /// <inheritdoc />
    public Task<Result<VolumeStatus>> DecreaseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Apply(
            endpoint => endpoint.MasterVolumeLevel =
                Math.Max(0f, endpoint.MasterVolumeLevel - StepPercentage),
            "I couldn't turn the volume down.");
    }

    /// <inheritdoc />
    public Task<Result<VolumeStatus>> SetMutedAsync(
        bool isMuted,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Apply(
            endpoint => endpoint.Mute = isMuted,
            isMuted ? "I couldn't mute the audio." : "I couldn't unmute the audio.");
    }

    private Task<Result<VolumeStatus>> Apply(Action<AudioEndpointVolume> change, string failureMessage)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            using var volume = device.AudioEndpointVolume;

            change(volume);

            return Task.FromResult(Result<VolumeStatus>.Success(Read(volume)));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Changing the output volume failed (HRESULT 0x{Result:X8}).",
                exception.HResult);
            return Task.FromResult(Result<VolumeStatus>.Failure(failureMessage));
        }
    }

    private Result<VolumeStatus> Read()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        using var volume = device.AudioEndpointVolume;

        return Result<VolumeStatus>.Success(Read(volume));
    }

    /// <summary>
    /// Reads the endpoint's own volume and mute state. Per-application session mute is
    /// deliberately ignored: the user asking whether they are muted means the system, and
    /// another application's session is not the assistant's business.
    /// </summary>
    private static VolumeStatus Read(AudioEndpointVolume volume) => new(
        (int)Math.Round(volume.MasterVolumeLevel, MidpointRounding.AwayFromZero),
        volume.Mute);
}
