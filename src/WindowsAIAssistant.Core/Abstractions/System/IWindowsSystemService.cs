using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Core.Abstractions.System;

/// <summary>
/// Provides safe, informational Windows system operations.
/// Mutating or privileged operations are intentionally excluded.
/// </summary>
public interface IWindowsSystemService
{
    /// <summary>Gets information about the current Windows system.</summary>
    Task<SystemInformation> GetSystemInformationAsync(
        CancellationToken cancellationToken = default);
}