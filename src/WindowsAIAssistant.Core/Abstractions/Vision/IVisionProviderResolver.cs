using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Vision;

namespace WindowsAIAssistant.Core.Abstractions.Vision;

/// <summary>
/// Chooses which provider will look at an image.
/// <para>
/// The single place that compares what this machine can do against what a request needs, so
/// that "no model accepts images" is reported as a configuration fact in the status line rather
/// than discovered when someone asks what is on their screen.
/// </para>
/// </summary>
public interface IVisionProviderResolver
{
    /// <summary>
    /// Chooses a provider for a request.
    /// </summary>
    /// <returns>
    /// The provider, or a failed result whose code says why none was chosen: no consent, no
    /// provider, or a model that cannot see.
    /// </returns>
    Result<IVisionProvider> Resolve(VisionPrivacyContext privacy);

    /// <summary>Reports what this machine can do with a screen right now.</summary>
    Task<VisionCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default);
}
