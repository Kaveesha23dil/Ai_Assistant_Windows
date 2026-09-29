using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models.Vision;
using WindowsAIAssistant.Core.Vision;
using IVisionProvider = WindowsAIAssistant.Core.Abstractions.Vision.IVisionProvider;

namespace WindowsAIAssistant.Infrastructure.Vision.Providers;

/// <summary>
/// A provider that describes an image without a network, a key, or a model.
/// <para>
/// Not a stub. It is the provider that runs when no cloud credential is configured, and its job
/// is to answer honestly from what is on the machine — which, on a fresh install, is whatever
/// the local recogniser read. Someone asking what is on their screen before they have pasted an
/// API key gets a real answer about their own screen rather than a "configure a provider" error,
/// and the rest of the pipeline is exercised end to end on every machine in every test run.
/// </para>
/// <para>
/// It never claims to have seen anything it did not. With no model behind it, the honest answer
/// is a description of what was recovered locally and an explicit statement that the rest needs
/// a model, which is what <see cref="ScreenAnalysisMapper"/>'s not-visible marker is for.
/// </para>
/// </summary>
public sealed class MockVisionProvider : IVisionProvider
{
    /// <summary>How much recovered text is included in an answer.</summary>
    private const int TextExcerptLength = 400;

    /// <inheritdoc />
    public string Name => "On-device (no model)";

    /// <inheritdoc />
    public bool IsCloudHosted => false;

    /// <inheritdoc />
    /// <remarks>
    /// False, and this is the one field where being wrong in either direction would matter.
    /// <para>
    /// It does not look at pixels: it can only report what the local recogniser read, which is
    /// not the same as describing a screen. Claiming image support would put "I can look at your
    /// screen" in the status line on a machine with no model, which is the sort of thing that
    /// looks like a small lie right up until somebody relies on it.
    /// </para>
    /// <para>
    /// False also does not disqualify it. The resolver uses this provider anyway as the honest
    /// local answer when there is no model — "I read this much text, and I can't describe the
    /// rest" — because a refusal leaves somebody with nothing at all.
    /// </para>
    /// </remarks>
    public bool SupportsImageInput => false;

    /// <inheritdoc />
    /// <remarks>
    /// Always true. This provider has no credential to be missing and nothing to be unreachable,
    /// and reporting otherwise would make "describe this screen" fail on a machine that can
    /// perfectly well do it.
    /// </remarks>
    public bool IsAvailable => true;

    /// <inheritdoc />
    /// <exception cref="ScreenVisionException">
    /// Never thrown. There is no remote call here that can fail, so there is no failure to
    /// translate, and a failure code from this type would always be a lie.
    /// </exception>
    public Task<Result<ScreenAnalysisResult>> AnalyzeAsync(
        ScreenAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!request.Privacy.AnalysisConsent)
        {
            throw new ScreenVisionException(
                "Looking at the screen is turned off, so the image was not read.",
                ErrorCodes.VisionAnalysisPermissionDenied);
        }

        var result = request.Ocr is { HasText: true }
            ? FromRecoveredText(request)
            : WithoutText(request);

        return Task.FromResult(Result<ScreenAnalysisResult>.Success(result));
    }

    /// <summary>
    /// Answers from what the local recogniser read, and says where the reading came from so a
    /// person can tell a transcription from a description.
    /// </summary>
    private static ScreenAnalysisResult FromRecoveredText(ScreenAnalysisRequest request)
    {
        var text = request.Ocr!.Text.Trim();
        var excerpt = text.Length <= TextExcerptLength
            ? text
            : string.Concat(text.AsSpan(0, TextExcerptLength), "…");

        var summary = $"I read this much text from the screen on this computer: \"{excerpt}\"";

        var (content, notVisible) = ScreenAnalysisMapper.Read(
            $"There is no vision model configured, so this is the text recovered from the image "
            + $"rather than a description of it.\n\n{summary}");

        return ScreenAnalysisMapper.Map(
            content,
            notVisible,
            providerName: "On-device (no model)",
            localText: text);
    }

    /// <summary>
    /// Answers with an honest "no model, nothing to read" rather than inventing a description.
    /// </summary>
    private static ScreenAnalysisResult WithoutText(ScreenAnalysisRequest request)
    {
        var reason = request.AnalysisType == ScreenAnalysisType.ExtractText
            ? "I couldn't read any text from that, and no vision model is configured to look at it."
            : "No vision model is configured, so I can only read text on this computer and not "
              + "describe what is on the screen. I read no text in this one.";

        return ScreenAnalysisMapper.Map(
            reason,
            notVisible: true,
            providerName: "On-device (no model)",
            warnings: request.AnalysisType == ScreenAnalysisType.Describe
                ? [ScreenWarningKind.ModelNotConfigured]
                : null);
    }
}
