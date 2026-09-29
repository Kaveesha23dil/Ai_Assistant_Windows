using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.Vision;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models.Vision;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

// The WinRT capture namespaces, named absolutely. This project has a
// WindowsAIAssistant.Infrastructure.Windows namespace of its own, and inside this namespace chain
// a plain "Windows.Graphics" binds to that one instead of the platform's.
using global::Windows.Graphics.Capture;

namespace WindowsAIAssistant.Infrastructure.Vision.Capture;

/// <summary>
/// Takes one screenshot, on request, and hands back the frame in memory.
/// <para>
/// Nothing here decides whether a screenshot may be taken: by the time this is called the
/// Application layer has already asked <c>PermissionService</c> and established that the person
/// turned the switch on. What this class is responsible for is the mechanics that need a
/// platform, and for reporting every way those mechanics can fail in a sentence somebody can act
/// on — including the one that is not a failure at all, which is a person closing the picker
/// because they changed their mind.
/// </para>
/// <para>
/// A session per request, a frame pool torn down before the method returns, and a buffer that is
/// encoded and handed over. There is no subscription, no polling, and no cache: a feature that
/// keeps a frame pool alive is a feature that is watching the screen, and this one is told what
/// to look at by a person, at the moment they ask.
/// </para>
/// </summary>
public sealed class ScreenCaptureService : IScreenCaptureService, IDisposable
{
    private readonly IScreenCaptureHost _host;
    private readonly GraphicsCaptureSessionManager _sessions;
    private readonly IOptionsMonitor<VisionOptions> _options;
    private readonly ILogger<ScreenCaptureService> _logger;

    private int _supportProbed;
    private bool _support;
    private bool _disposed;

    /// <remarks>
    /// The constructor is internal because it takes the capture session manager, which is an
    /// implementation detail of this assembly. Registration goes through
    /// <c>AddVisionServices</c>, which builds the manager and this service together so that a
    /// host never has to know that a graphics device is cached between requests.
    /// </remarks>
    internal ScreenCaptureService(
        IScreenCaptureHost host,
        GraphicsCaptureSessionManager sessions,
        IOptionsMonitor<VisionOptions> options,
        ILogger<ScreenCaptureService> logger)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _host = host;
        _sessions = sessions;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Gets a value indicating whether this machine can capture a frame at all.
    /// <para>
    /// Asked of the graphics device rather than assumed from the operating system version,
    /// because a virtual machine or a remote desktop session can be running a supported version
    /// of Windows with no adapter a capture could use, and "this computer cannot take a
    /// screenshot" is a better answer than a failure on first use. Probed once and remembered:
    /// the answer cannot change without the process restarting.
    /// </para>
    /// </summary>
    public bool IsSupported
    {
        get
        {
            if (Volatile.Read(ref _supportProbed) == 0)
            {
                var supported = GraphicsCaptureSessionManager.ProbeSupport();

                Volatile.Write(ref _support, supported);
                Volatile.Write(ref _supportProbed, 1);
            }

            return Volatile.Read(ref _support);
        }
    }

    /// <summary>
    /// Asks the person to choose what to capture, takes that one frame, and returns it encoded.
    /// <para>
    /// The choice is always the person's and always made through the Windows picker, even when
    /// the request already said which display it meant. There is no way to name a window by
    /// title or a display by index from here, and a program that could would eventually name the
    /// wrong one — the wrong window is somebody's private conversation.
    /// </para>
    /// </summary>
    /// <param name="request">What was asked for.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The frame, or a failed result whose code says which of the several distinguishable things
    /// went wrong. <see cref="ErrorCodes.ScreenCaptureCancelled"/> is the one that is not a fault
    /// at all, and the caller shows nothing for it.
    /// </returns>
    public async Task<Result<ScreenCaptureResult>> CaptureAsync(
        ScreenCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.CaptureType == ScreenCaptureType.None)
        {
            return Result<ScreenCaptureResult>.Failure(
                ErrorCodes.ScreenCaptureFailed,
                "I wasn't told what to take a screenshot of.");
        }

        if (!IsSupported)
        {
            return Result<ScreenCaptureResult>.Failure(
                ErrorCodes.ScreenCaptureUnsupported,
                "This computer doesn't have a graphics device I can take a screenshot with.");
        }

        GraphicsCaptureItem? item;

        try
        {
            item = await PickAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A person who closed the picker, or a request that was called off, lands back in
            // the resting state. It is not told about as a failure, because nothing failed.
            _logger.LogInformation("A screenshot request ended before a target was chosen.");
            throw;
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "The capture picker could not be shown.");
            return Result<ScreenCaptureResult>.Failure(
                ErrorCodes.ScreenCaptureFailed,
                "I couldn't open the window that asks what to take a screenshot of.");
        }
        catch (Exception exception) when (exception is not ScreenVisionException)
        {
            _logger.LogError(exception, "The capture picker failed.");
            return Result<ScreenCaptureResult>.Failure(
                ErrorCodes.ScreenCaptureFailed,
                "I couldn't ask what to take a screenshot of.");
        }

        if (item is null)
        {
            _logger.LogInformation("A screenshot request was dismissed at the picker.");
            return Result<ScreenCaptureResult>.Failure(
                ErrorCodes.ScreenCaptureCancelled,
                "No screenshot was taken.");
        }

        CapturedFrame? frame = null;
        byte[]? encoded = null;

        try
        {
            frame = await _sessions.CaptureAsync(item, cancellationToken).ConfigureAwait(false);

            if (frame.AppearsBlank())
            {
                // Windows hands over a frame for content it will not let an application read,
                // and that frame is solid black. Describing it would produce a confident
                // account of nothing, and retrying would not help: the refusal is the answer.
                _logger.LogInformation(
                    "The captured frame was blank, so the content is probably protected.");
                return Result<ScreenCaptureResult>.Failure(
                    ErrorCodes.ScreenProtectedContent,
                    "Windows didn't let me read that window. Protected content can't be "
                    + "screenshotted, and I won't try to work around it.");
            }

            var pixels = Extract(frame, request.Region, out var region, out var regionError);
            if (pixels is null)
            {
                return Result<ScreenCaptureResult>.Failure(
                    regionError ?? ErrorCodes.ScreenRegionInvalid,
                    "The part of the screenshot you selected isn't in the image.");
            }

            var width = region?.Width ?? frame.Width;
            var height = region?.Height ?? frame.Height;

            cancellationToken.ThrowIfCancellationRequested();
            encoded = await CapturedImageConverter
                .EncodePngAsync(pixels, width, height, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var capture = new ScreenCaptureResult(
                encoded,
                width,
                height,
                ScreenPixelFormat.Bgra8,
                request.CaptureType,
                DateTimeOffset.UtcNow,
                item.DisplayName,
                region);

            // Ownership has moved to the caller. Nothing below this point may clear the buffer,
            // or the screenshot they are about to look at would be a row of zeroes.
            encoded = null;
            frame = null;

            _logger.LogInformation(
                "Captured a {CaptureType} of {Width}x{Height} pixels on request.",
                request.CaptureType,
                width,
                height);

            return Result<ScreenCaptureResult>.Success(capture);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ImageProcessingException exception)
        {
            _logger.LogError(exception, "A captured frame could not be encoded.");
            return Result<ScreenCaptureResult>.Failure(
                exception.ErrorCode,
                "I couldn't read the screen I just captured.");
        }
        catch (ScreenVisionException exception)
        {
            _logger.LogError(exception, "A screen capture failed.");
            return Result<ScreenCaptureResult>.Failure(
                exception.ErrorCode,
                "I couldn't take that screenshot.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "A screen capture failed in an unexpected way.");
            return Result<ScreenCaptureResult>.Failure(
                ErrorCodes.ScreenCaptureFailed,
                "I couldn't take that screenshot.");
        }
        finally
        {
            // The frame holds raw pixels of somebody's screen, and so does the encoded copy if
            // the encoding never happened. Both are overwritten rather than dropped, so neither
            // is still readable in freed memory for the rest of the session.
            frame?.Dispose();
            encoded?.AsSpan().Clear();
        }
    }

    /// <summary>
    /// Shows the Windows capture picker on the host window's thread and waits for a choice.
    /// </summary>
    private async Task<GraphicsCaptureItem?> PickAsync(CancellationToken cancellationToken)
    {
        var timeout = _options.CurrentValue.CaptureTimeoutSeconds;

        using var expiry = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        expiry.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, timeout)));

        try
        {
            // The expiry token is passed in rather than merely armed alongside. Arming a token
            // source and then not handing the token to the thing being waited on produces a
            // timeout that expires in silence while the picker is still on screen, which is the
            // one outcome the timeout exists to prevent.
            return await _host.InvokeOnHostThreadAsync(async () =>
            {
                // Constructed here, on the host's thread, because a picker is a user interface
                // object and can only be created and shown by a thread with a message loop.
                var picker = new GraphicsCapturePicker();

                // Given a parent window, or it opens behind the application, takes focus away
                // from whatever the person was doing, and can stay on screen after the request
                // that opened it has gone.
                var handle = _host.WindowHandle;
                if (handle != IntPtr.Zero)
                {
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
                }

                return await picker.PickSingleItemAsync().AsTask(expiry.Token).ConfigureAwait(false);
            }, expiry.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The picker outlived the timeout. Treated exactly as a dismissal, because from the
            // caller's point of view the person is no longer there to choose anything.
            _logger.LogInformation("The capture picker was closed without a choice being made.");
            return null;
        }
    }

    /// <summary>
    /// Takes the pixels for the result, cropped when the person chose a region.
    /// <para>
    /// The crop is applied to the frame, in the frame's own pixels, before encoding. Applied
    /// afterwards to the encoded image it would be a second crop path with its own idea of which
    /// coordinate system it was in, and a highlight drawn over one thing would be answered as
    /// though it were over another.
    /// </para>
    /// </summary>
    /// <returns>
    /// Tightly packed blue-first pixels, or <see langword="null"/> when the rectangle cannot be
    /// applied, with the reason in <paramref name="errorCode"/>.
    /// </returns>
    private static byte[]? Extract(
        CapturedFrame frame,
        ScreenRegion? requested,
        out ScreenRegion? region,
        out string? errorCode)
    {
        if (requested is null)
        {
            region = null;
            errorCode = null;
            return frame.Pixels.ToArray();
        }

        if (!ScreenRegion.TryNormalize(requested, frame.Width, frame.Height, out region, out var error)
            || region is null)
        {
            errorCode = error;
            return null;
        }

        errorCode = null;
        return frame.ExtractRegion(region.X, region.Y, region.Width, region.Height);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _sessions.Dispose();
    }
}
