using System.Globalization;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Application.Vision;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Abstractions.Vision;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Vision;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Voice.Commands.Screen;

/// <summary>
/// Handles spoken requests that need to look at the screen.
/// <para>
/// Four separate intents rather than one, because the four reach different systems and need
/// different permissions. "What's on my screen" needs the model and the screen-analysis consent;
/// "what does it say" needs only the local recogniser and works with the network off; "what does
/// this error mean" is the same as the first with a different instruction; and a region request
/// is the same again with a different target. Folding them into one handler with a flag would
/// make it possible to reach a cloud model while believing the local text path was being used.
/// </para>
/// <para>
/// The consent decision is not made here. It is settled once inside the analysis service, which
/// knows whether the provider in use is on this machine; this handler only translates a refusal
/// back into something speakable. Re-deciding it here would let the two disagree, and the
/// disagreement would show up as voice working while the page refuses.
/// </para>
/// <para>
/// Nothing about the screen is put in the result data either. The answer is spoken, and the data
/// carries the size of the capture and the warnings, because a screenshot or its text in a
/// command history is exactly the persistence this feature is meant to avoid.
/// </para>
/// </summary>
public sealed class ScreenVoiceHandler : VoiceHandlerBase
{
    private const int MaximumSpokenCharacters = 400;

    private static readonly AssistantIntent[] Supported =
    [
        AssistantIntent.DescribeScreen,
        AssistantIntent.ReadScreenText,
        AssistantIntent.AskAboutScreen,
        AssistantIntent.ExplainScreenError,
        AssistantIntent.AnalyzeScreenRegion,
        AssistantIntent.SaveScreenshot,
    ];

    private readonly IScreenAnalysisService _screen;
    private readonly IScreenCaptureService _capture;
    private readonly IScreenshotStore _store;

    public ScreenVoiceHandler(
        IScreenAnalysisService screen,
        IScreenCaptureService capture,
        IScreenshotStore store,
        IPermissionService permissions,
        ILogger<ScreenVoiceHandler> logger)
        : base(permissions, logger)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(store);

        _screen = screen;
        _capture = capture;
        _store = store;
    }

    /// <inheritdoc />
    public override IReadOnlyCollection<AssistantIntent> Intents => Supported;

    /// <inheritdoc />
    public override async Task<VoiceCommandResult> ExecuteAsync(
        VoiceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        // Saving is not analysis, and it does not go near the analysis service. It is the one
        // intent in this feature whose whole purpose is to put pixels on a disk, so it is the one
        // that most needs its own path rather than being expressed as a task for a model.
        if (command.Intent == AssistantIntent.SaveScreenshot)
        {
            return await SaveAsync(command, cancellationToken).ConfigureAwait(false);
        }

        var options = BuildOptions(command, out var refusal);
        if (options is null)
        {
            return refusal!;
        }

        try
        {
            var result = options.AnalysisType == ScreenAnalysisType.ExtractText
                ? await _screen.ReadScreenTextAsync(options, cancellationToken).ConfigureAwait(false)
                : await _screen.AnalyzeScreenAsync(options, cancellationToken).ConfigureAwait(false);

            if (result.IsFailure || result.Value is null)
            {
                return DescribeFailure(command, result.ErrorCode, result.ErrorMessage);
            }

            return Speak(command, options, result.Value);
        }
        catch (OperationCanceledException)
        {
            // The picker was dismissed. Choosing not to look is an answer, not a fault, and the
            // base class's blanket handler would otherwise turn it into a failure.
            throw;
        }
        catch (Exception exception)
        {
            return Failed(command, exception);
        }
    }

    /// <summary>
    /// Captures what the person chose and writes it where they can find it.
    /// <para>
    /// A full display, because "save a screenshot" is spoken without a pointer and there is
    /// nothing to point at. The person gets the Windows picker first, so they still choose the
    /// window or display; what they do not get is a highlight rectangle to draw, which a spoken
    /// request has no way to express.
    /// </para>
    /// <para>
    /// The frame is disposed as soon as the write finishes, whether it succeeded or not. A
    /// screenshot left in a disposed-or-not argument on a failure path is a screenshot of
    /// somebody's bank statement still readable in memory for the rest of the session.
    /// </para>
    /// </summary>
    private async Task<VoiceCommandResult> SaveAsync(
        VoiceCommand command,
        CancellationToken cancellationToken)
    {
        if (!Permissions.IsGranted(PermissionCapability.ScreenCapture))
        {
            return Denied(command, PermissionCapability.ScreenCapture);
        }

        try
        {
            var captured = await _capture
                .CaptureAsync(ScreenCaptureRequest.ForDisplay(), cancellationToken)
                .ConfigureAwait(false);

            if (captured.IsFailure || captured.Value is null)
            {
                return DescribeFailure(command, captured.ErrorCode, captured.ErrorMessage);
            }

            using var capture = captured.Value;

            var saved = await _store
                .SaveAsync(capture, destinationFolder: null, suggestedFileName: null, cancellationToken)
                .ConfigureAwait(false);

            if (saved.IsFailure || saved.Value is not { } path)
            {
                return saved.IsSuccess
                    ? Unavailable(command, "I couldn't work out where to save that screenshot.")
                    : VoiceCommandResult.Failure(command, saved.ErrorCode!, saved.ErrorMessage!);
            }

            // The folder, not the file. A spoken file name is long, and it is a fact about the
            // person's disk that does not need to be in a voice history to be useful.
            return VoiceCommandResult.Success(
                command,
                $"Screenshot saved to {Path.GetFileName(Path.GetDirectoryName(path) ?? path)}.",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["filePath"] = path,
                    ["sizeInBytes"] = capture.ImageBytes.Length.ToString(CultureInfo.InvariantCulture),
                });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Failed(command, exception);
        }
    }

    /// <summary>
    /// Maps an intent onto the task and the target, taking the person's own words from the
    /// transcript where they were supplied.
    /// </summary>
    private ScreenAnalysisRequestOptions? BuildOptions(VoiceCommand command, out VoiceCommandResult? refusal)
    {
        var question = command.GetParameter(VoiceCommand.QueryParameter);

        switch (command.Intent)
        {
            case AssistantIntent.DescribeScreen:
                refusal = null;
                return new ScreenAnalysisRequestOptions(ScreenAnalysisType.Describe);

            case AssistantIntent.ReadScreenText:
                refusal = null;
                return new ScreenAnalysisRequestOptions(ScreenAnalysisType.ExtractText);

            case AssistantIntent.ExplainScreenError:
                refusal = null;
                return new ScreenAnalysisRequestOptions(ScreenAnalysisType.ExplainError);

            case AssistantIntent.AskAboutScreen:
                if (string.IsNullOrWhiteSpace(question))
                {
                    refusal = Invalid(command, "I didn't catch what you wanted to ask about the screen.");
                    return null;
                }

                refusal = null;
                return new ScreenAnalysisRequestOptions(ScreenAnalysisType.AnswerQuestion, userQuestion: question);

            case AssistantIntent.AnalyzeScreenRegion:
                if (string.IsNullOrWhiteSpace(question))
                {
                    refusal = Invalid(command, "I didn't catch what you wanted to ask about that part of the screen.");
                    return null;
                }

                refusal = null;
                return new ScreenAnalysisRequestOptions(
                    ScreenAnalysisType.AnswerQuestion,
                    ScreenCaptureType.SelectedRegion,
                    question);

            default:
                refusal = Invalid(command, "I didn't understand that screen request.");
                return null;
        }
    }

    /// <summary>
    /// Turns a refusal into something speakable, keeping a permission refusal distinguishable
    /// from a fault so the voice path can say which switch to go and turn on.
    /// </summary>
    private VoiceCommandResult DescribeFailure(
        VoiceCommand command,
        string? errorCode,
        string? errorMessage)
    {
        var message = errorMessage ?? "I couldn't look at your screen.";

        var isRefusal = errorCode is ErrorCodes.VisionAnalysisPermissionDenied
            or ErrorCodes.VisionCloudPermissionDenied
            or ErrorCodes.OcrPermissionDenied
            or ErrorCodes.ScreenCaptureUnsupported
            or ErrorCodes.VisionModelNoImageSupport;

        if (isRefusal)
        {
            Logger.LogInformation(
                "Voice action {Intent} was refused: {ErrorCode}.",
                command.Intent,
                errorCode);

            return VoiceCommandResult.Failure(
                command,
                errorCode!,
                message,
                isPermissionDenied: true);
        }

        // A dismissed picker never reaches here, so a failure with no code at all is a fault.
        return Unavailable(command, message);
    }

    /// <summary>
    /// Speaks the answer, capped, and reports only metadata in the result data.
    /// </summary>
    private VoiceCommandResult Speak(
        VoiceCommand command,
        ScreenAnalysisRequestOptions options,
        ScreenAnalysisResult result)
    {
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["analysisType"] = options.AnalysisType.ToString(),
            ["provider"] = result.Provider ?? "none",
            ["warnings"] = result.Warnings.Count.ToString(CultureInfo.InvariantCulture),
        };

        if (result.Warnings.Contains(ScreenWarningKind.SensitiveContentSkipped))
        {
            data["sensitiveContentSkipped"] = "true";
        }

        // A transcription is read out as-is: it is text that was on the screen, and shortening it
        // would make the spoken answer differ from the one shown.
        var text = options.AnalysisType == ScreenAnalysisType.ExtractText
            ? result.ExtractedText ?? result.BuildDisplayText()
            : result.BuildDisplayText();

        return VoiceCommandResult.Success(command, Speakable(text), data);
    }

    /// <summary>
    /// Trims an answer to something worth listening to, cutting at a sentence or a space rather
    /// than mid-word, and saying that it was cut.
    /// </summary>
    private static string Speakable(string text)
    {
        var trimmed = text.Trim();

        if (trimmed.Length <= MaximumSpokenCharacters)
        {
            return trimmed;
        }

        var cut = trimmed[..MaximumSpokenCharacters];
        var lastStop = cut.LastIndexOfAny(['.', '!', '?']);
        if (lastStop > MaximumSpokenCharacters / 2)
        {
            cut = cut[..(lastStop + 1)];
        }
        else
        {
            var lastSpace = cut.LastIndexOf(' ');
            if (lastSpace > 0)
            {
                cut = cut[..lastSpace];
            }
        }

        return $"{cut.TrimEnd()} The rest is on the visual assistant panel.";
    }
}
