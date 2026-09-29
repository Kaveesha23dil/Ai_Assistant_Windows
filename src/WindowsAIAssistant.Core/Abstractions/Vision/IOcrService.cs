using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Vision;

namespace WindowsAIAssistant.Core.Abstractions.Vision;

/// <summary>
/// Reads text out of an image, on this machine.
/// <para>
/// Local by definition. This is the interface that keeps "read what's on my screen" working
/// with the network unplugged and every cloud permission switched off, so an implementation is
/// never allowed to call out to anything.
/// </para>
/// </summary>
public interface IOcrService
{
    /// <summary>
    /// Gets a value indicating whether any text engine is usable here.
    /// <para>
    /// Probed rather than assumed. A machine with no recogniser reports <see langword="false"/>
    /// and the visual path carries on without it, because a screenshot is still a screenshot.
    /// </para>
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Gets which engine will be used, resolved once and cached.
    /// <para>
    /// Reported so the status line can say "reading with the built-in engine" rather than
    /// failing later with a message nobody can act on.
    /// </para>
    /// </summary>
    OcrProviderKind ProviderKind { get; }

    /// <summary>
    /// Reads the text in an encoded image.
    /// <para>
    /// Returns <see cref="OcrResult.Empty"/> rather than throwing when there is no engine or no
    /// text. An empty answer is a valid answer to "what does it say", and distinguishing it from
    /// a fault is the caller's job via <see cref="OcrResult.Provider"/>.
    /// </para>
    /// </summary>
    Task<OcrResult> ReadTextAsync(
        ReadOnlyMemory<byte> imageBytes,
        CancellationToken cancellationToken = default);
}
