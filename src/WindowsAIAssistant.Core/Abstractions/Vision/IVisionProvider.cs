using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Vision;

namespace WindowsAIAssistant.Core.Abstractions.Vision;

/// <summary>
/// Looks at an image and says something about it.
/// <para>
/// Provider-independent and permission-aware, and both of those are load-bearing. The interface
/// is the only thing the Application layer knows about, so a local model and a cloud API are
/// interchangeable; and because the request it receives carries the consent in force, an
/// implementation can refuse to send an image it was handed without it, rather than trusting
/// that some layer further up already checked.
/// </para>
/// </summary>
public interface IVisionProvider
{
    /// <summary>Gets the name shown in status text, for example "OpenAI (gpt-4o)".</summary>
    string Name { get; }

    /// <summary>
    /// Gets a value indicating whether images leave this machine when this provider is used.
    /// <para>
    /// Not a preference. The consent gate reads this rather than asking the provider to police
    /// itself, so turning a switch off cannot depend on the provider remembering to check it.
    /// </para>
    /// </summary>
    bool IsCloudHosted { get; }

    /// <summary>
    /// Gets a value indicating whether the configured model accepts an image at all.
    /// <para>
    /// A text-only model is a supported configuration, not a failure, and asking it to read a
    /// screenshot is answered with an explanation rather than an exception.
    /// </para>
    /// </summary>
    bool SupportsImageInput { get; }

    /// <summary>Gets a value indicating whether the provider is configured and reachable.</summary>
    bool IsAvailable { get; }

    /// <summary>Looks at an image and answers.</summary>
    /// <exception cref="ScreenVisionException">
    /// Thrown with a code from <see cref="ErrorCodes"/> for a refusal, a missing capability, or
    /// a failure. Never thrown for a cancellation.
    /// </exception>
    Task<Result<ScreenAnalysisResult>> AnalyzeAsync(
        ScreenAnalysisRequest request,
        CancellationToken cancellationToken = default);
}
