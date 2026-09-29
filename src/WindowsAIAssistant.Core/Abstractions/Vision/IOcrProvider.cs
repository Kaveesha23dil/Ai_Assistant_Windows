using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Vision;

namespace WindowsAIAssistant.Core.Abstractions.Vision;

/// <summary>
/// One engine that can read text out of an image.
/// <para>
/// A provider rather than a service, so the choice of engine is a decision made once by a
/// resolver instead of a chain of type tests inside the reading path. Each engine declares
/// whether it is usable at all, which is how a model that has to be installed is never chosen
/// merely for being newest.
/// </para>
/// </summary>
public interface IOcrProvider
{
    /// <summary>Gets which engine this is.</summary>
    OcrProviderKind Kind { get; }

    /// <summary>
    /// Gets a value indicating whether this engine can be used right now, without installing
    /// anything the person has not agreed to.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Gets a value indicating whether the first call may need to download a model.
    /// <para>
    /// Used to decide whether to ask first. Consent to read the screen is not consent to fetch
    /// a hundred megabytes from the store, and the resolver treats these differently.
    /// </para>
    /// </summary>
    bool RequiresModelDownload { get; }

    /// <summary>
    /// Reads the text in an encoded image.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the engine cannot run on this machine, so a caller that skipped
    /// <see cref="IsAvailable"/> gets told rather than given a silent empty answer.
    /// </exception>
    Task<OcrResult> ReadAsync(
        ReadOnlyMemory<byte> imageBytes,
        CancellationToken cancellationToken = default);
}
