using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Core.Abstractions.Voice;

/// <summary>
/// A bounded, in-memory record of which voice actions ran and whether they succeeded.
/// <para>
/// The history stores no transcript, no response text, and no clipboard or AI content, so it
/// can answer "what did the assistant just do" without retaining what was said. When the
/// user has not opted in to history, implementations keep nothing at all.
/// </para>
/// </summary>
public interface IVoiceCommandHistory
{
    /// <summary>Gets the retained entries, newest first.</summary>
    IReadOnlyCollection<VoiceCommandHistoryEntry> Entries { get; }

    /// <summary>Gets a value indicating whether entries are currently being retained.</summary>
    bool IsEnabled { get; }

    /// <summary>Records the outcome of a command. A no-op while history is disabled.</summary>
    void Record(VoiceCommandResult result);

    /// <summary>Discards every retained entry.</summary>
    void Clear();
}
