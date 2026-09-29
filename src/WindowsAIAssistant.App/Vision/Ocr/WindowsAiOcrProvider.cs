using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.AI;
using Microsoft.Windows.AI.Imaging;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models.Vision;
using WindowsAIAssistant.Infrastructure.Vision.Ocr;
using ImageBuffer = Microsoft.Graphics.Imaging.ImageBuffer;
using IOcrProvider = WindowsAIAssistant.Core.Abstractions.Vision.IOcrProvider;
using OcrTextRegion = WindowsAIAssistant.Core.Models.Vision.OcrTextRegion;

namespace WindowsAIAssistant.App.Vision.Ocr;

/// <summary>
/// Reads text with the newer Windows text recogniser, when this machine has it.
/// <para>
/// This type lives in the application project rather than in Infrastructure for one reason: the
/// Windows AI text APIs are reachable here, through the application SDK, and adding a second
/// package reference to the lowest layer so that it can reach something above it would invert
/// the whole dependency arrangement for a convenience. The interface it implements is the same
/// one the older recogniser implements, and nothing above this file can tell which is which.
/// </para>
/// <para>
/// Availability is decided once and cached, and it is decided by asking Windows rather than by
/// inspecting a build number. On a machine that has never seen this feature, the first call can
/// report any of seven different reasons it will not work — a missing capability, a disabled
/// policy, a system update, hardware that is not fast enough — and each of them means the same
/// thing here, which is that the older recogniser should be used instead. The specific reason is
/// logged, never shown, because "your chipset is not supported" is not a thing a person can act
/// on mid-sentence.
/// </para>
/// <para>
/// Nothing is downloaded without being asked for. The recogniser's model is a feature of Windows
/// and may be acquired the first time it is used, so <see cref="RequiresModelDownload"/> is true
/// and the resolver declines to select this engine unless a person allowed it in settings.
/// </para>
/// </summary>
public sealed class WindowsAiOcrProvider : IOcrProvider, IDisposable
{
    private readonly ILogger<WindowsAiOcrProvider> _logger;
    private readonly Lock _gate = new();

    private bool _probed;
    private bool _available;
    private TextRecognizer? _recognizer;
    private bool _disposed;

    public WindowsAiOcrProvider(ILogger<WindowsAiOcrProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public OcrProviderKind Kind => OcrProviderKind.WindowsAi;

    /// <summary>
    /// The longest edge handed to this recogniser.
    /// <para>
    /// Not read from the recogniser, because this one publishes no limit — unlike the older
    /// Windows recogniser, which does. It matches the longest edge the rest of the feature
    /// reduces images to, so a screenshot that arrived here already reduced is not reduced
    /// again, and one that arrived whole is brought to the same size the person would have seen
    /// in the preview.
    /// </para>
    /// </summary>
    private const uint MaximumImageDimension = 2560;

    /// <inheritdoc />
    public string Name => "Windows text recogniser";

    /// <inheritdoc />
    /// <remarks>
    /// True. The model is acquired the first time the recogniser is created rather than being
    /// part of the operating system image, and a feature that can fetch something on first use
    /// has to be described that way before it is selected, not after.
    /// </remarks>
    public bool RequiresModelDownload => true;

    /// <inheritdoc />
    public bool IsAvailable
    {
        get
        {
            if (_disposed)
            {
                return false;
            }

            lock (_gate)
            {
                if (_probed)
                {
                    return _available;
                }

                _probed = true;
                _available = Probe(out var reason);
                _logger.LogInformation(
                    "The Windows text recogniser {Availability} on this machine ({Reason}).",
                    _available ? "is available" : "is not available",
                    reason);

                return _available;
            }
        }
    }

    /// <inheritdoc />
    /// <exception cref="ScreenVisionException">
    /// Thrown with a code from <see cref="ErrorCodes"/> when the recogniser could not be created
    /// or refused the image. Never thrown for a missing recogniser, which is a reason to return
    /// nothing rather than a failure.
    /// </exception>
    public async Task<OcrResult> ReadAsync(
        ReadOnlyMemory<byte> imageBytes,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsAvailable)
        {
            return OcrResult.Empty;
        }

        var recognizer = await GetOrCreateAsync().ConfigureAwait(false);
        if (recognizer is null)
        {
            return OcrResult.Empty;
        }

        // The recogniser takes an image buffer rather than encoded bytes, and the same prepared
        // bitmap is used whether the image needed scaling first or not, so there is one path here
        // and no chance of a decode happening at a different size from the one measured.
        using var bitmap = await OcrImageFactory
            .CreateAsync(imageBytes, MaximumImageDimension, cancellationToken)
            .ConfigureAwait(false);

        using var buffer = ImageBuffer.CreateForSoftwareBitmap(bitmap);

        RecognizedText? recognized = null;

        try
        {
            recognized = await recognizer
                .RecognizeTextFromImageAsync(buffer)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsRecognizerUnusable(exception))
        {
            // A recogniser that has failed once will fail again. Dropping it means the next
            // request builds a new one, and the resolver's cached decision still points at the
            // older engine, so this is a graceful degradation rather than a loop.
            _logger.LogWarning(
                "The Windows text recogniser failed and has been discarded for this session. "
                + "Text reading will use the built-in recogniser from now on.");

            Discard();

            throw new ScreenVisionException(
                "The Windows text recogniser could not read that image.",
                ErrorCodes.OcrFailed,
                exception);
        }
        catch (Exception exception)
        {
            throw new ScreenVisionException(
                "The screenshot could not be read as an image for text recognition.",
                ErrorCodes.OcrFailed,
                exception);
        }

        return Map(recognized);
    }

    /// <summary>
    /// Asks Windows whether the recogniser is usable, and explains the refusal in a word.
    /// <para>
    /// Every failure state is a refusal, not an error, and the reason is a single word in a log
    /// line. A machine without the capability and a machine with the capability switched off
    /// behave the same way here, because from this application's side they are the same
    /// situation: there is no text recogniser, and the older one will do.
    /// </para>
    /// </summary>
    private bool Probe(out string reason)
    {
        reason = "unknown";

        try
        {
            var state = TextRecognizer.GetReadyState();

            switch (state)
            {
                case AIFeatureReadyState.Ready:
                    reason = "ready";
                    return true;

                case AIFeatureReadyState.NotReady:
                    // Ready on this machine, merely not yet initialised. Treated as available,
                    // because creating it below is what starts that, and refusing here would
                    // mean this engine was never used anywhere.
                    reason = "not initialised yet";
                    return true;

                case AIFeatureReadyState.NotSupportedOnCurrentSystem:
                    reason = "not supported by this build of Windows";
                    return false;

                case AIFeatureReadyState.DisabledByUser:
                    reason = "turned off in system settings";
                    return false;

                case AIFeatureReadyState.CapabilityMissing:
                    reason = "the text recognition capability is not installed";
                    return false;

                case AIFeatureReadyState.NotCompatibleWithSystemHardware:
                    reason = "this machine's hardware is not supported";
                    return false;

                case AIFeatureReadyState.OSUpdateNeeded:
                    reason = "a Windows update is required";
                    return false;

                default:
                    reason = $"unrecognised state {(int)state}";
                    return false;
            }
        }
        catch (Exception exception) when (IsRecognizerUnusable(exception))
        {
            // An unpackaged application on a build that has never heard of these APIs throws from
            // the very first call. That is a refusal, not a fault in this program.
            reason = $"not present ({exception.GetType().Name})";
            return false;
        }
        catch (Exception exception)
        {
            reason = $"could not be checked ({exception.GetType().Name})";
            return false;
        }
    }

    /// <summary>
    /// Creates the recognizer once and keeps it.
    /// <para>
    /// Held for the life of the process because creating one is the expensive part, and the
    /// model it needs is the expensive part after that. A screenshot every ten seconds does not
    /// want to pay either repeatedly.
    /// </para>
    /// <para>
    /// Awaited rather than waited on. A recognizer is created through a projected asynchronous
    /// call, and blocking a thread for it on a UI thread is a deadlock waiting for a slow enough
    /// disk; this is the only place in this project that could have done that, and it does not.
    /// </para>
    /// </summary>
    private async Task<TextRecognizer?> GetOrCreateAsync()
    {
        lock (_gate)
        {
            if (_recognizer is not null)
            {
                return _recognizer;
            }
        }

        TextRecognizer? created;

        try
        {
            created = await TextRecognizer.CreateAsync().AsTask().ConfigureAwait(false);
        }
        catch (Exception exception) when (IsRecognizerUnusable(exception))
        {
            _logger.LogWarning(
                "The Windows text recognizer could not be created ({Reason}); the built-in "
                + "recogniser will be used instead.",
                exception.GetType().Name);

            lock (_gate)
            {
                // Marked unavailable so the resolver's next question is answered immediately
                // rather than trying this again on every screenshot for the rest of the session.
                _available = false;
            }

            return null;
        }

        lock (_gate)
        {
            // A second request may have created one while this was awaiting. Whoever got there
            // first keeps it, and the loser is disposed rather than leaked.
            if (_recognizer is not null)
            {
                created.Dispose();
                return _recognizer;
            }

            _recognizer = created;
            return created;
        }
    }

    /// <summary>
    /// Drops the recognizer so a later request can build a fresh one.
    /// </summary>
    private void Discard()
    {
        lock (_gate)
        {
            _available = false;
            _recognizer?.Dispose();
            _recognizer = null;
        }
    }

    /// <summary>
    /// Reads the recognised text into a result, keeping a bounding box per line.
    /// </summary>
    private static OcrResult Map(RecognizedText? recognized)
    {
        if (recognized is null)
        {
            return OcrResult.Empty;
        }

        var text = new System.Text.StringBuilder();
        var regions = new List<OcrTextRegion>();

        // A recogniser reports a line, not a character, and a line is the unit a person would
        // point at. A line with no usable geometry is left out of the boxes but is still in the
        // text above, so dropping it from here costs a highlight and nothing else. The text is
        // assembled from the lines rather than read from the result, because this recogniser
        // publishes no whole-string property — the lines are the only reading of it there is.
        foreach (var line in recognized.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Text))
            {
                continue;
            }

            if (text.Length > 0)
            {
                text.Append('\n');
            }

            text.Append(line.Text);

            if (Bounds(line) is { } bounds)
            {
                regions.Add(new OcrTextRegion(line.Text, bounds, double.NaN));
            }
        }

        if (text.Length == 0)
        {
            return OcrResult.Empty;
        }

        // Neither this recogniser nor the older one reports a per-line confidence, so the result
        // says so rather than claiming full confidence it does not have.
        return new OcrResult(
            text.ToString(),
            OcrProviderKind.WindowsAi,
            language: null,
            confidence: double.NaN,
            regions);
    }

    /// <summary>
    /// Turns a line's point cloud into a rectangle, or <see langword="null"/> when it has none.
    /// </summary>
    private static ScreenRegion? Bounds(RecognizedLine line)
    {
        var box = line.BoundingBox;
        var topLeft = box.TopLeft;
        var bottomRight = box.BottomRight;

        var width = (int)Math.Round(bottomRight.X - topLeft.X);
        var height = (int)Math.Round(bottomRight.Y - topLeft.Y);

        if (width <= 0 || height <= 0)
        {
            return null;
        }

        return new ScreenRegion(
            Math.Max(0, (int)Math.Round(topLeft.X)),
            Math.Max(0, (int)Math.Round(topLeft.Y)),
            width,
            height);
    }

    /// <summary>
    /// Decides whether an exception means "this recogniser is not usable" rather than "this
    /// request failed".
    /// <para>
    /// The projection surfaces almost everything as a COM failure, including a type that does not
    /// exist on this build of Windows. A missing type and a genuine failure would otherwise look
    /// identical, and the response to each is different: one is a machine to fall back from, the
    /// other is a sentence for a person.
    /// </para>
    /// </summary>
    private static bool IsRecognizerUnusable(Exception exception) =>
        exception is COMException
        || exception is TypeLoadException
        || exception is DllNotFoundException
        || exception is EntryPointNotFoundException
        || exception is PlatformNotSupportedException
        || exception is InvalidOperationException
        || exception.HResult == unchecked((int)0x80040154);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (_gate)
        {
            _recognizer?.Dispose();
            _recognizer = null;
        }
    }
}
