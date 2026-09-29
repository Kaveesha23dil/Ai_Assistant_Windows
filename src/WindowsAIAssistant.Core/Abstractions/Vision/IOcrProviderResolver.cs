using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Abstractions.Vision;

/// <summary>
/// Chooses which text engine to use, once, and explains itself when it finds none.
/// <para>
/// Separate from <see cref="IOcrService"/> so that "which engine is this machine using" is
/// answerable without running a recognition, and so that the order of preference is a single
/// visible list of engines rather than a chain of conditionals spread across the reading path.
/// </para>
/// </summary>
public interface IOcrProviderResolver
{
    /// <summary>
    /// Gets the engine to use, or <see cref="OcrProviderKind.None"/> when none is usable.
    /// <para>
    /// Resolution is cached: a capability probe is comparatively expensive, and a recogniser
    /// becoming available mid-session is rare enough that the settings page re-resolves on open.
    /// </para>
    /// </summary>
    OcrProviderKind Resolve();

    /// <summary>
    /// Gets a value indicating whether the best available engine would download a model on first
    /// use, so the person can be asked before anything is fetched.
    /// </summary>
    bool RequiresModelDownload { get; }

    /// <summary>Gets a user-safe line describing the outcome, for the status area.</summary>
    Result<string> DescribeAvailability();
}
