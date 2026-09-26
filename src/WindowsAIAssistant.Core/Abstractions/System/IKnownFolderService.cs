using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Abstractions.System;

/// <summary>
/// Resolves a well-known user folder to its absolute path.
/// <para>
/// Only the <see cref="KnownFolderKind"/> identifier is shared. No path is ever built from a
/// user name or a hand-assembled string, so the resolution stays correct for any account and
/// any redirect setup.
/// </para>
/// </summary>
public interface IKnownFolderService
{
    /// <summary>Returns the absolute path of the requested well-known folder.</summary>
    Task<Result<string>> GetPathAsync(
        KnownFolderKind folder,
        CancellationToken cancellationToken = default);
}
