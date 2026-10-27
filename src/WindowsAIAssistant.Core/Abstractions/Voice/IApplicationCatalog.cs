using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Core.Abstractions.Voice;

/// <summary>
/// Lists applications that are installed or advertised by Windows.
/// </summary>
public interface IApplicationCatalog
{
    /// <summary>Gets the launchable applications on this machine.</summary>
    Task<IReadOnlyList<ApplicationTarget>> GetInstalledApplicationsAsync(
        CancellationToken cancellationToken = default);
}
