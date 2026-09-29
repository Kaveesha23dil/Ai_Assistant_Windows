using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Application.Vision;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Abstractions.Vision;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models.Vision;

namespace WindowsAIAssistant.Application.Vision;

/// <summary>
/// Runs a screen request end to end: consent, capture, local text, model, answer.
/// <para>
/// One sequence, used by the overlay, the chat box, and the microphone alike. That is the whole
/// design goal: the privacy of a screenshot should not depend on which of the three asked for
/// it. A spoken "what's on my screen" goes through exactly the checks a typed one does, and a
/// typed question about a document never touches this path at all.
/// </para>
/// <para>
/// The ordering below is the ordering of the privacy rules rather than the ordering that would
/// be most convenient. Consent is resolved before a frame is taken, so a refusal costs nothing
/// and no pixels exist to worry about. The image is prepared only after the model that would
/// receive it has been chosen, so a request that will be refused never pays for encoding. And
/// the raw frame is disposed in a finally block, so the one path that could retain a screenshot
/// in memory is a finally block rather than something a later edit could skip.
/// </para>
/// </summary>
public sealed class ScreenAnalysisService : IScreenAnalysisService
{
    private readonly IScreenCaptureService _capture;
    private readonly IOcrService _ocr;
    private readonly IImagePreprocessor _preprocessor;
    private readonly IVisionProviderResolver _providers;
    private readonly ScreenConsentPolicy _consent;
    private readonly IScreenContextService _context;
    private readonly IPermissionService _permissions;
    private readonly ILogger<ScreenAnalysisService> _logger;

    /// <summary>
    /// Guards the whole request. Two concurrent requests would mean two capture pickers, and
    /// <see cref="_activeCapture"/> could be disposed out from under the request still using it.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private ScreenCaptureResult? _activeCapture;

    public ScreenAnalysisService(
        IScreenCaptureService capture,
        IOcrService ocr,
        IImagePreprocessor preprocessor,
        IVisionProviderResolver providers,
        ScreenConsentPolicy consent,
        IScreenContextService context,
        IPermissionService permissions,
        ILogger<ScreenAnalysisService> logger)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(ocr);
        ArgumentNullException.ThrowIfNull(preprocessor);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(consent);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(logger);

        _capture = capture;
        _ocr = ocr;
        _preprocessor = preprocessor;
        _providers = providers;
        _consent = consent;
        _context = context;
        _permissions = permissions;
        _logger = logger;
    }

    /// <summary>
    /// Raised as the request moves through its phases, so a person can see what is happening
    /// without a poll. The state carries no image and no recognized text.
    /// </summary>
    public event EventHandler<ScreenAssistantState>? StateChanged;

    /// <inheritdoc />
    public async Task<Result<ScreenAnalysisResult>> AnalyzeScreenAsync(
        ScreenAnalysisRequestOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Reading text is the one task that never needs a model, so it is answered from the
        // local recogniser whatever else is configured. This is checked before anything else so
        // that "what does it say" works on a machine with no credential, no network, and every
        // cloud permission turned off.
        if (options.AnalysisType == ScreenAnalysisType.ExtractText)
        {
            return await ReadScreenTextAsync(options, cancellationToken).ConfigureAwait(false);
        }

        var consent = await _consent.ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (consent.IsFailure || consent.Value is null)
        {
            return Result<ScreenAnalysisResult>.Failure(
                consent.ErrorCode ?? ErrorCodes.VisionAnalysisPermissionDenied,
                consent.ErrorMessage ?? "Looking at your screen is not turned on.");
        }

        var privacy = consent.Value;

        var provider = _providers.Resolve(privacy);
        if (provider.IsFailure || provider.Value is null)
        {
            return Result<ScreenAnalysisResult>.Failure(
                provider.ErrorCode ?? ErrorCodes.VisionProviderUnavailable,
                provider.ErrorMessage ?? "No provider is set up that can look at an image.");
        }

        if (!provider.Value.SupportsImageInput)
        {
            return Result<ScreenAnalysisResult>.Failure(
                ErrorCodes.VisionModelNoImageSupport,
                "The configured model reads text but cannot look at images. " +
                "You can still ask it to read the text on your screen.");
        }

        return await ExecuteAsync(options, privacy, provider.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<ScreenAnalysisResult>> ReadScreenTextAsync(
        ScreenAnalysisRequestOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Reading text needs the screen read, but not the model, so it is gated on capture alone.
        // Gating it on screen analysis as well would mean that turning off the model switches
        // also switched off a feature that never contacts a model.
        if (!_permissions.IsGranted(PermissionCapability.ScreenCapture))
        {
            return Result<ScreenAnalysisResult>.Failure(
                ErrorCodes.OcrPermissionDenied,
                "Taking a screenshot is not turned on. Turn on \"allow screen capture\" in Settings.");
        }

        if (!_permissions.IsGranted(PermissionCapability.ScreenOcr))
        {
            return Result<ScreenAnalysisResult>.Failure(
                ErrorCodes.OcrPermissionDenied,
                "Reading text from your screen is not turned on. Turn on \"read text on screen\" in Settings.");
        }

        if (!_ocr.IsAvailable)
        {
            return Result<ScreenAnalysisResult>.Failure(
                ErrorCodes.OcrUnavailable,
                "This computer has no text recognition available, so the text on your screen cannot be read.");
        }

        var privacy = VisionPrivacyContext.LocalOnly;

        return await ExecuteAsync(
            options,
            privacy,
            provider: null,
            cancellationToken,
            skipModel: true).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<VisionCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
        _providers.GetCapabilitiesAsync(cancellationToken);

    /// <summary>
    /// The single path every request takes once it has consent and a provider.
    /// <para>
    /// Written once on purpose. Two near-identical implementations of "capture, read, answer"
    /// are two opportunities for one of them to skip the text screening or the disposal, and
    /// that is precisely the class of bug that cannot be seen in a screenshot.
    /// </para>
    /// </summary>
    private async Task<Result<ScreenAnalysisResult>> ExecuteAsync(
        ScreenAnalysisRequestOptions options,
        VisionPrivacyContext privacy,
        IVisionProvider? provider,
        CancellationToken cancellationToken,
        bool skipModel = false)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Any frame still held from a previous request is erased before a new one is taken.
            // Taking a second screenshot before releasing the first is how a session ends up
            // holding several, and a screenshot left in freed memory is still readable.
            ReleaseActiveCapture();

            Report(ScreenAssistantPhase.Capturing, "Taking a screenshot…");

            var captureResult = await CaptureAsync(options, cancellationToken).ConfigureAwait(false);
            if (captureResult.IsFailure || captureResult.Value is null)
            {
                Report(ScreenAssistantPhase.Idle);
                return Result<ScreenAnalysisResult>.Failure(
                    captureResult.ErrorCode ?? ErrorCodes.ScreenCaptureFailed,
                    captureResult.ErrorMessage ?? "The screenshot could not be taken.");
            }

            _activeCapture = captureResult.Value;
            var warnings = new List<ScreenWarningKind>();

            try
            {
                Report(ScreenAssistantPhase.Processing, "Reading the text on screen…");

                var (ocr, redacted) = await ReadTextAsync(cancellationToken).ConfigureAwait(false);
                warnings.AddRange(ScreenSafetyFilter.WarningsFor(ocr, redacted));

                var source = VisualSourceReference.FromCapture(_activeCapture);
                var ocrText = ocr?.Text;

                if (skipModel || provider is null)
                {
                    _context.Hold(source, ocrText, privacy.AllowsCloudImageSubmission);
                    Report(ScreenAssistantPhase.Completed, "Finished reading the screen.");

                    return Result<ScreenAnalysisResult>.Success(new ScreenAnalysisResult(
                        summary: DescribeTextOutcome(ocr),
                        extractedText: ocrText,
                        provider: ocr is null ? null : ocr.Provider.ToString(),
                        warnings: warnings));
                }

                var prepared = await PrepareAsync(cancellationToken).ConfigureAwait(false);
                if (prepared.WasResized)
                {
                    warnings.Add(ScreenWarningKind.ImageResized);
                }

                var request = new ScreenAnalysisRequest(
                    prepared.ImageBytes,
                    options.AnalysisType,
                    privacy,
                    options.UserQuestion,
                    ocr,
                    prepared.Width,
                    prepared.Height,
                    captureType: _activeCapture.CaptureType,
                    region: _activeCapture.Region);

                // Consent is recorded as it was actually exercised, and the frame is disposed
                // on the way out. Both matter: the first means a follow-up is governed by the
                // decision made for that frame rather than today's settings, and the second
                // means there is no image left for a follow-up to send even if it wanted to.
                _context.Hold(source, ocrText, privacy.AllowsCloudImageSubmission);

                Report(ScreenAssistantPhase.Analyzing, "Looking at the screenshot…");

                var analysis = await provider.AnalyzeAsync(request, cancellationToken).ConfigureAwait(false);
                if (analysis.IsFailure || analysis.Value is null)
                {
                    Report(
                        ScreenAssistantPhase.Error,
                        null,
                        analysis.ErrorCode ?? ErrorCodes.VisionAnalysisFailed);

                    return Result<ScreenAnalysisResult>.Failure(
                        analysis.ErrorCode ?? ErrorCodes.VisionAnalysisFailed,
                        analysis.ErrorMessage ?? "The screenshot could not be analyzed.");
                }

                var value = analysis.Value.WithWarnings(warnings);

                Report(ScreenAssistantPhase.Completed, "Finished.");
                return Result<ScreenAnalysisResult>.Success(value);
            }
            finally
            {
                // The only place a raw frame is ever released. Being in a finally block is the
                // point: every exit, including a cancellation and a provider that throws, goes
                // through it, and there is no other statement in the application that erases
                // screen pixels.
                ReleaseActiveCapture();
            }
        }
        catch (OperationCanceledException)
        {
            // A person who closed the picker or pressed Escape has not encountered a fault, so
            // the assistant returns to idle and raises nothing. Turning a refusal to look into
            // an error message is the kind of small wrongness that teaches people the app is
            // unreliable.
            ReleaseActiveCapture();
            Report(ScreenAssistantPhase.Idle);
            throw;
        }
        catch (ScreenVisionException exception)
        {
            ReleaseActiveCapture();
            Report(ScreenAssistantPhase.Error, exception.Message, exception.ErrorCode);
            return Result<ScreenAnalysisResult>.Failure(exception.ErrorCode, exception.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Takes the frame, translating a dismissed picker into a cancellation rather than an error.
    /// </summary>
    private async Task<Result<ScreenCaptureResult>> CaptureAsync(
        ScreenAnalysisRequestOptions options,
        CancellationToken cancellationToken)
    {
        if (!_capture.IsSupported)
        {
            return Result<ScreenCaptureResult>.Failure(
                ErrorCodes.ScreenCaptureUnsupported,
                "This version of Windows cannot take a screenshot in the way this app needs.");
        }

        var request = options.CaptureType switch
        {
            ScreenCaptureType.Window => ScreenCaptureRequest.ForWindow(),
            ScreenCaptureType.SelectedRegion => ScreenCaptureRequest.ForDisplay(),
            _ => ScreenCaptureRequest.ForDisplay()
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ScreenVisionLimits.CaptureTimeout);

        var result = await _capture.CaptureAsync(request, timeout.Token).ConfigureAwait(false);

        if (result.IsFailure &&
            string.Equals(result.ErrorCode, ErrorCodes.ScreenCaptureCancelled, StringComparison.Ordinal))
        {
            // Nothing was captured because the person said no. Not an error, and not a
            // cancellation either: the request simply ends, and the caller's finally still runs.
            throw new OperationCanceledException("The screenshot picker was dismissed.");
        }

        return result;
    }

    /// <summary>
    /// Reads the text on the current frame, screening it before anything else can see it.
    /// <para>
    /// Returns null rather than failing when no engine is available. A screenshot is still worth
    /// describing without its text, and turning the absence of a local recogniser into a refusal
    /// would make image analysis depend on a Windows component that may not be installed.
    /// </para>
    /// </summary>
    private async Task<(OcrResult? Ocr, bool Redacted)> ReadTextAsync(CancellationToken cancellationToken)
    {
        var capture = _activeCapture;
        if (capture is null || !_ocr.IsAvailable)
        {
            return (null, false);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ScreenVisionLimits.OcrTimeout);

        try
        {
            var result = await _ocr.ReadTextAsync(capture.ImageBytes, timeout.Token).ConfigureAwait(false);
            var (screened, redacted) = ScreenSafetyFilter.Screen(result);
            return (screened, redacted);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Recognition timed out on its own rather than being cancelled by the person. The
            // image is still perfectly analysable, so this is reported and shrugged off.
            _logger.LogInformation("Local text recognition did not finish in time; continuing without it.");
            return (null, false);
        }
        catch (Exception exception)
        {
            // An engine that throws is a bug worth knowing about, but the person asked a question
            // about their screen and the picture answers it. The exception is logged; the frame
            // and the request carry on.
            _logger.LogWarning(exception, "Local text recognition failed; continuing with the image alone.");
            return (null, false);
        }
    }

    /// <summary>Prepares the frame for sending, sized and encoded within the fixed limits.</summary>
    private async Task<PreparedImage> PrepareAsync(CancellationToken cancellationToken)
    {
        var capture = _activeCapture ?? throw new ScreenVisionException(
            "There is no screenshot to analyze.",
            ErrorCodes.ScreenCaptureFailed);

        return await _preprocessor
            .PrepareAsync(capture.ImageBytes, capture.Width, capture.Height, cancellationToken)
            .ConfigureAwait(false);
    }

    private void ReleaseActiveCapture()
    {
        var capture = Interlocked.Exchange(ref _activeCapture, null);
        capture?.Dispose();
    }

    private void Report(
        ScreenAssistantPhase phase,
        string? statusMessage = null,
        string? errorCode = null,
        VisualSourceReference? source = null) =>
        StateChanged?.Invoke(this, new ScreenAssistantState(phase, statusMessage, errorCode, source));

    /// <summary>
    /// Describes the outcome of a local read without a model, saying plainly what happened when
    /// nothing was found rather than returning an empty answer that looks like a result.
    /// </summary>
    private static string DescribeTextOutcome(OcrResult? ocr) => ocr switch
    {
        null => "The text on the screen could not be read on this computer.",
        { HasText: true } => "Text was read from the screen on this computer.",
        _ => "No text was found in the screenshot."
    };
}
