using WindowsAIAssistant.Core.Common;

namespace WindowsAIAssistant.Core.Abstractions.System;

/// <summary>
/// Launches applications installed on the Windows system by name or executable.
/// </summary>
public interface IApplicationLauncherService
{
    /// <summary>Launches the specified application.</summary>
    Task<Result> LaunchAsync(
        string applicationName,
        CancellationToken cancellationToken = default);
}