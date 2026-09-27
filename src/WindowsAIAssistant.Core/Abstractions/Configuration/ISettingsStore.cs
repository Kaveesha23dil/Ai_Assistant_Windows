using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Core.Abstractions.Configuration;

/// <summary>
/// Keeps the settings a person changes between sessions.
/// <para>
/// The store is written to synchronously enough to be trusted on the way out of a save, and
/// is expected to be readable by the next launch of the same user only. It is not a
/// synchronisation point, an audit log, or a place for anything the person did not ask to
/// be remembered.
/// </para>
/// </summary>
public interface ISettingsStore
{
    /// <summary>
    /// Gets a description of where the settings live, shown to the person so they know which
    /// file to look at or delete.
    /// </summary>
    string Location { get; }

    /// <summary>
    /// Writes the settings, replacing what was there. Implementations report a failure rather
    /// than throwing, and must leave the previous settings intact when they do.
    /// </summary>
    Task<Result> SaveAsync(UserSettings settings, CancellationToken cancellationToken = default);
}
