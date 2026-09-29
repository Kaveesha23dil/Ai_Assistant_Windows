using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models.Vision;

// The WinRT text recognition types, named absolutely and aliased. This project has a
// WindowsAIAssistant.Infrastructure.Windows namespace of its own, and inside this namespace
// chain a plain "Windows.Media" binds to that one instead of the platform's. The aliases matter
// for a second reason as well: three of these four names also exist in this application, with
// different meanings, and an unaliased import would silently pick the WinRT one — so
// ReadAsync would appear to return a Windows.Graphics.Imaging object rather than a reading.
using SoftwareBitmap = global::Windows.Graphics.Imaging.SoftwareBitmap;
using WinRtOcrEngine = global::Windows.Media.Ocr.OcrEngine;
using WinRtOcrResult = global::Windows.Media.Ocr.OcrResult;
using IOcrProvider = WindowsAIAssistant.Core.Abstractions.Vision.IOcrProvider;

namespace WindowsAIAssistant.Infrastructure.Vision.Ocr;

/// <summary>
/// Reads text on this machine with the recogniser that has been in Windows for years.
/// <para>
/// The fallback, and the one that works everywhere. It is here because "read what's on my
/// screen" is the promise that matters most when the network is unplugged, and a promise that
/// depends on a downloadable model or a capable processor is a promise that quietly stops
/// being kept on somebody's machine. A model that is newer and better on half the computers
/// installed is a worse answer than one that is slightly worse on all of them.
/// </para>
/// <para>
/// The engine is created once and kept, because building one loads a language resource and
/// handing that back to the operating system between every question would make a two-second
/// capture take noticeably longer. It is local, it is in-process, and it has no member that
/// could reach a network.
/// </para>
/// </summary>
public sealed class WindowsLegacyOcrProvider : IOcrProvider, IDisposable
{
    private readonly ILogger<WindowsLegacyOcrProvider> _logger;
    private readonly Lock _gate = new();

    private WinRtOcrEngine? _engine;
    private bool _probed;
    private bool _disposed;

    public WindowsLegacyOcrProvider(ILogger<WindowsLegacyOcrProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public OcrProviderKind Kind => OcrProviderKind.WindowsLegacy;

    /// <summary>
    /// Gets a value indicating whether this engine can be used right now.
    /// <para>
    /// Probed once by asking for an engine for the person's own profile languages. That is the
    /// honest test: it is true on a machine with the language packs installed and false on one
    /// where recognition has nothing to recognise with, and no version number predicts which.
    /// </para>
    /// </summary>
    public bool IsAvailable
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return GetEngine() is not null;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the first call may need to download a model. Never: this
    /// recogniser is part of Windows and asks for nothing.
    /// </summary>
    public bool RequiresModelDownload => false;

    /// <inheritdoc />
    /// <exception cref="ScreenVisionException">
    /// Thrown with a code from <see cref="ErrorCodes"/> when the image cannot be read. The
    /// absence of a recogniser is not an exception here: a caller that skipped
    /// <see cref="IsAvailable"/> gets an empty result, which it can tell from a real answer by
    /// looking at <see cref="OcrResult.Provider"/>.
    /// </exception>
    public async Task<OcrResult> ReadAsync(
        ReadOnlyMemory<byte> imageBytes,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var engine = GetEngine();
        if (engine is null)
        {
            _logger.LogInformation("Text recognition was asked for on a machine with no recogniser.");
            return OcrResult.Empty;
        }

        if (imageBytes.IsEmpty)
        {
            throw new ScreenVisionException(
                "There was no image to read text from.",
                ErrorCodes.ScreenCaptureEmpty);
        }

        // The engine's own ceiling, not the application's: this recogniser refuses anything
        // larger outright rather than reducing it, so anything above the limit has to be scaled
        // down before it is handed over rather than reported as a failure.
        var limit = (uint)Math.Max(1, WinRtOcrEngine.MaxImageDimension);

        SoftwareBitmap? bitmap = null;

        try
        {
            bitmap = await OcrImageFactory
                .CreateAsync(imageBytes, limit, cancellationToken)
                .ConfigureAwait(false);

            var recognized = await engine.RecognizeAsync(bitmap).AsTask().ConfigureAwait(false);
            return Map(recognized, bitmap.PixelWidth, bitmap.PixelHeight);
        }
        catch (ImageProcessingException exception)
        {
            throw new ScreenVisionException(
                "I couldn't read that image.",
                exception.ErrorCode,
                exception);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A recogniser that will not read an image reports it as a COM failure. The sentence
            // is fixed so it is the same whichever Windows build produced it, and nothing about
            // the image itself is echoed into the message or the log.
            _logger.LogError(exception, "Windows text recognition failed.");
            throw new ScreenVisionException(
                "Windows couldn't read any text out of that image.",
                ErrorCodes.OcrFailed,
                exception);
        }
        finally
        {
            bitmap?.Dispose();
        }
    }

    /// <summary>
    /// Converts a recogniser's answer into the application's own shape.
    /// <para>
    /// One box per word rather than per line, because a line box is useless for pointing at
    /// something. The text is reported with <see cref="double.NaN"/> confidence throughout,
    /// because this engine reports none — and reporting its silence as zero would read as a
    /// very poor score rather than as an absent one.
    /// </para>
    /// </summary>
    private static OcrResult Map(
        WinRtOcrResult recognized,
        int imageWidth,
        int imageHeight)
    {
        var regions = new List<OcrTextRegion>();

        foreach (var line in recognized.Lines)
        {
            foreach (var word in line.Words)
            {
                var bounds = OcrImageFactory.NormalizeBounds(word.BoundingRect, imageWidth, imageHeight);
                if (bounds is null || string.IsNullOrWhiteSpace(word.Text))
                {
                    continue;
                }

                regions.Add(new OcrTextRegion(word.Text, bounds));
            }
        }

        return new OcrResult(
            recognized.Text ?? string.Empty,
            OcrProviderKind.WindowsLegacy,
            RecognizerLanguageTag(),
            double.NaN,
            regions);
    }

    private static string? RecognizerLanguageTag()
    {
        try
        {
            return WinRtOcrEngine.TryCreateFromUserProfileLanguages()?.RecognizerLanguage.LanguageTag;
        }
        catch (Exception)
        {
            // Purely informational. Failing to name the language must not fail a reading that
            // has already been done.
            return null;
        }
    }

    /// <summary>
    /// Returns the shared engine, creating it on first use.
    /// </summary>
    private WinRtOcrEngine? GetEngine()
    {
        if (Volatile.Read(ref _probed))
        {
            return _engine;
        }

        lock (_gate)
        {
            if (_probed)
            {
                return _engine;
            }

            WinRtOcrEngine? created = null;

            try
            {
                created = WinRtOcrEngine.TryCreateFromUserProfileLanguages();
            }
            catch (Exception exception)
            {
                // Recorded once, at the point of the failure, and reported as a capability that
                // is absent rather than as an error the person did not cause.
                _logger.LogInformation(
                    exception,
                    "No Windows text recogniser could be created for this machine.");
            }

            _engine = created;
            Volatile.Write(ref _probed, true);

            return _engine;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _engine = null;
        Volatile.Write(ref _probed, true);
    }
}
