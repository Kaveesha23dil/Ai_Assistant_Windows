using WindowsAIAssistant.Core.Models.Vision;

namespace WindowsAIAssistant.Core.Abstractions.Vision;

/// <summary>
/// Holds the one frame the person is currently talking about, for as long as they are talking
/// about it.
/// <para>
/// A single slot, deliberately. A list of recent screenshots would be a screenshot history, and
/// a screenshot history is surveillance with a cache: it survives the request that created it,
/// it is available to anything that can read this object, and nobody asked for it. One frame,
/// replaced on each request, disposed on the next, and never written to disk without a separate
/// instruction, is a thing a person can reason about.
/// </para>
/// <para>
/// The image itself is deliberately not exposed here. Callers get the description, the text that
/// was read from it, and the ability to clear it — enough to render a chip, answer a follow-up,
/// and forget it, without a second path to the pixels.
/// </para>
/// </summary>
public interface IScreenContextService
{
    /// <summary>
    /// Raised whenever the held frame changes, including when it is cleared.
    /// <para>
    /// Carries no image, so a listener that wants to know it must come and read the properties.
    /// </para>
    /// </summary>
    event EventHandler? ContextChanged;

    /// <summary>Gets a value indicating whether a frame is held right now.</summary>
    bool HasScreen { get; }

    /// <summary>Gets what is being talked about, or <see langword="null"/> when nothing is.</summary>
    VisualSourceReference? Source { get; }

    /// <summary>Gets the text read from the held frame on this machine, or <see langword="null"/>.</summary>
    string? ExtractedText { get; }

    /// <summary>
    /// Gets whether the held frame is allowed to be sent to a cloud provider.
    /// <para>
    /// Carried on the frame so that a follow-up typed into the chat box is governed by the
    /// consent given for that frame, rather than by whatever the chat settings happen to be.
    /// </para>
    /// </summary>
    bool AllowsCloudSubmission { get; }

    /// <summary>
    /// Holds a frame's description, text, and consent, and erases any previous one.
    /// <para>
    /// Does not take the frame. The caller keeps ownership of the pixels and disposes them when
    /// it is done, so there is exactly one owner of the buffer and it is never a singleton.
    /// </para>
    /// </summary>
    void Hold(VisualSourceReference source, string? extractedText, bool allowsCloudSubmission);

    /// <summary>Forgets the held frame, erasing anything read from it.</summary>
    void Clear();
}
