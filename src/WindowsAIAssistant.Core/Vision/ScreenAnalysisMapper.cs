using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Vision;

namespace WindowsAIAssistant.Core.Vision;

/// <summary>
/// Turns a provider's reply into a typed result.
/// <para>
/// Isolated because the marker convention is a contract. A provider is asked to answer
/// <c>SCREEN_NOT_VISIBLE</c> when the screenshot does not contain the answer, and a provider
/// that ignores the convention should produce "I couldn't see that on your screen" rather than
/// being taken at its word. Reading the marker out in one place means a change to how providers
/// are instructed does not have to be matched in several.
/// </para>
/// <para>
/// In Core beside the prompt builder, because the convention is shared by the code that asks
/// for an answer and the code that reads one. If the two were in different layers the marker
/// would be spelled twice, and a typo in the copy would be indistinguishable from a provider
/// that did not understand the instruction.
/// </para>
/// </summary>
public static class ScreenAnalysisMapper
{
    /// <summary>The marker a provider is asked to use when the content is not on the screen.</summary>
    public const string NotVisibleMarker = VisionPromptBuilder.NotVisibleMarker;

    /// <summary>What to say when a provider used the marker and said nothing else.</summary>
    private const string NothingVisibleMessage = "I couldn't find that on your screen.";

    /// <summary>
    /// Reads a provider's reply and says whether it used the not-visible marker.
    /// </summary>
    /// <remarks>
    /// The marker is only honoured on its own, or on the first line. A provider that mentions
    /// the token in passing while explaining something is not reporting that it saw nothing, and
    /// treating that as a refusal would discard the useful part of its answer.
    /// </remarks>
    public static (string Content, bool NotVisible) Read(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var trimmed = content.Trim();
        if (trimmed.Length == 0)
        {
            return (NothingVisibleMessage, true);
        }

        var firstLine = trimmed.Split('\n', 2)[0].Trim();
        if (!string.Equals(firstLine, NotVisibleMarker, StringComparison.OrdinalIgnoreCase))
        {
            return (trimmed, false);
        }

        var rest = trimmed[firstLine.Length..].Trim();
        return (rest.Length == 0 ? NothingVisibleMessage : rest, true);
    }

    /// <summary>
    /// Maps a provider's reply onto a result, applying the marker convention and the warnings
    /// gathered along the way.
    /// </summary>
    /// <param name="content">The provider's reply, already read by <see cref="Read"/>.</param>
    /// <param name="notVisible">Whether the provider reported the content was not on screen.</param>
    /// <param name="providerName">Which provider said it, for the status line.</param>
    /// <param name="localText">
    /// Text read on this machine, or null. Preferred over the model's transcription for literal
    /// extraction, because a model asked to read small print produces a plausible and wrong
    /// version of it while the recogniser's version is at least checkable.
    /// </param>
    /// <param name="warnings">Warnings gathered before the provider was called.</param>
    public static ScreenAnalysisResult Map(
        string content,
        bool notVisible,
        string? providerName = null,
        string? localText = null,
        IEnumerable<ScreenWarningKind>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        var collected = warnings?.ToList() ?? [];

        // Not visible is a qualification, not a failure: the model was asked and answered, and
        // the person is told what it did and did not see. Reporting it as an error would put a
        // dialog in front of somebody who did nothing wrong.
        if (notVisible)
        {
            collected.Add(ScreenWarningKind.TextNotRecognized);
        }

        var isExtraction = !string.IsNullOrWhiteSpace(localText) && notVisible;

        return new ScreenAnalysisResult(
            summary: content,
            answer: content,
            extractedText: isExtraction ? localText : null,
            provider: providerName,
            warnings: collected);
    }
}
