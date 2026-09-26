using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Voice.Services;

/// <summary>
/// A bounded in-memory record of which voice actions ran.
/// <para>
/// Entries hold the intent, the outcome, and the timing, and nothing else. The transcript,
/// the spoken response, clipboard text, and AI prompts are all excluded on purpose so the
/// history can never become a log of what the user said. When history is switched off the
/// recorder keeps nothing at all.
/// </para>
/// </summary>
public sealed class VoiceCommandHistory : IVoiceCommandHistory
{
    /// <summary>The maximum number of retained entries before the oldest are discarded.</summary>
    public const int Capacity = 100;

    private readonly ConcurrentQueue<VoiceCommandHistoryEntry> _entries = new();
    private readonly ILogger<VoiceCommandHistory> _logger;

    public VoiceCommandHistory(ILogger<VoiceCommandHistory> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsEnabled { get; set; }

    /// <inheritdoc />
    public IReadOnlyCollection<VoiceCommandHistoryEntry> Entries => _entries.ToArray();

    /// <inheritdoc />
    public void Record(VoiceCommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!IsEnabled)
        {
            return;
        }

        _entries.Enqueue(new VoiceCommandHistoryEntry(
            result.CommandId,
            result.Intent,
            result.SafetyLevel,
            result.IsSuccess,
            result.ErrorCode,
            result.CompletedAt));

        while (_entries.Count > Capacity)
        {
            if (_entries.TryDequeue(out _))
            {
                continue;
            }

            break;
        }

        _logger.LogDebug(
            "Voice history recorded intent {Intent} with success {Succeeded}.",
            result.Intent,
            result.IsSuccess);
    }

    /// <inheritdoc />
    public void Clear()
    {
        while (_entries.TryDequeue(out _))
        {
            continue;
        }
    }
}
