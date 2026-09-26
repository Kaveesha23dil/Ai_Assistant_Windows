using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Voice;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.Voice;

/// <summary>
/// Placeholder for local wake-phrase detection.
/// <para>
/// The abstraction exists so a future engine can be added without touching the listening
/// pipeline, and this implementation deliberately does nothing. No local wake-word engine is
/// installed, and a stub that pretended to detect a phrase would be worse than saying so:
/// starting fails with a clear explanation and the feature stays off by default.
/// </para>
/// <para>
/// The interface offers no way to stream microphone audio anywhere, so a cloud wake-word
/// service cannot be introduced through this surface. Any future implementation must run
/// entirely on the machine.
/// </para>
/// </summary>
public sealed class WakeWordService : IWakeWordService
{
    private readonly IOptionsMonitor<VoiceOptions> _options;
    private readonly ILogger<WakeWordService> _logger;

    public WakeWordService(IOptionsMonitor<VoiceOptions> options, ILogger<WakeWordService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsEnabled => _options.CurrentValue.WakeWordEnabled;

    /// <inheritdoc />
    public IReadOnlyCollection<string> WakePhrases => _options.CurrentValue.WakePhrases;

    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <summary>
    /// Never raised: no local engine is installed, so no phrase can be detected. The event is
    /// declared with an explicit empty implementation to say that plainly rather than leaving
    /// a warning for a subscriber that cannot exist yet.
    /// </summary>
    public event EventHandler<WakeWordMatch>? WakeWordDetected
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public Task<Result> StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsEnabled)
        {
            return Task.FromResult(Result.Failure("Wake word detection is switched off."));
        }

        _logger.LogInformation(
            "Wake word detection was requested but no local detection engine is installed.");

        return Task.FromResult(Result.Failure(
            "Wake word detection needs a local detection engine, which isn't installed."));
    }

    /// <inheritdoc />
    public Task<Result> StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result.Success());
    }
}
