using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Core.Abstractions.Voice;

/// <summary>
/// Turns the human name a user speaks into an application the machine can actually launch.
/// <para>
/// This is the allow list that makes application launching safe. The voice layer supplies a
/// name such as "chrome" and only a value returned from here reaches the shell, so a spoken
/// sentence can never be interpreted as a command line.
/// </para>
/// </summary>
public interface IApplicationResolver
{
    /// <summary>
    /// Resolves a spoken application name.
    /// </summary>
    /// <param name="name">A human name such as "chrome", "vs code", or "file explorer".</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The resolved target, or a failure when the name is not recognized.</returns>
    Task<Result<ApplicationTarget>> ResolveAsync(
        string name,
        CancellationToken cancellationToken = default);
}
