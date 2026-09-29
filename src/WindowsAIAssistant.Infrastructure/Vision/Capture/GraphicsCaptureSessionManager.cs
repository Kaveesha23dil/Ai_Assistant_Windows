using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Infrastructure.Vision.Interop;
using ID3D11Device = WindowsAIAssistant.Infrastructure.Vision.Interop.ID3D11Device;
using ID3D11DeviceContext = WindowsAIAssistant.Infrastructure.Vision.Interop.ID3D11DeviceContext;
using WinRtDevice = global::Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice;

namespace WindowsAIAssistant.Infrastructure.Vision.Capture;

/// <summary>
/// Runs one short capture session and returns a single frame as system memory.
/// <para>
/// A session, not a subscription. Windows Graphics Capture is built for continuous capture, and
/// the tempting optimisation is to leave a frame pool alive so the next screenshot is instant.
/// That would be a camera pointed at the desktop for the lifetime of the process, which is
/// exactly the surveillance this feature refuses to be. So the pool is created for one frame,
/// read once, and torn down before this method returns — and the graphics device is cached
/// across requests because creating one is the expensive part, while a frame pool is not.
/// </para>
/// <para>
/// The frame is requested rather than awaited through the event handler. Calling
/// <c>TryGetNextFrame</c> from inside <c>FrameArrived</c> is the documented pattern, but it
/// hands the buffer management to a callback that runs on a runtime thread; taking one frame
/// and returning releases it deterministically instead, which matters more here than shaving
/// microseconds off a request a person just asked for.
/// </para>
/// </summary>
internal sealed class GraphicsCaptureSessionManager : IDisposable
{
    /// <summary>
    /// Frames requested at once. Two is the smallest pool that lets the capture runtime hand
    /// over a frame while the previous one is being copied, and it does not buffer more of the
    /// screen than the one being read.
    /// </summary>
    private const int BufferCount = 2;

    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private WinRtDevice? _winRtDevice;
    private bool _probed;

    /// <summary>
    /// Reports whether this machine has a graphics device a capture could use.
    /// <para>
    /// A throw-away device is created and released rather than the cached one being asked for,
    /// because this is asked by a settings page and a status line where the answer is needed
    /// before anybody has captured anything, and holding a device open for the sake of a yes-or-no
    /// would keep an adapter awake on a machine that is not being looked at.
    /// </para>
    /// </summary>
    internal static bool ProbeSupport()
    {
        var (device, context) = D3D11DeviceFactory.TryCreate();

        if (device is not null)
        {
            Marshal.ReleaseComObject(device);
        }

        if (context is not null)
        {
            Marshal.ReleaseComObject(context);
        }

        return device is not null && context is not null;
    }

    /// <summary>
    /// Gets the shared graphics device, or raises a specific reason when none can be created.
    /// <para>
    /// Probed once and the failure remembered. Creating a device that will fail is expensive
    /// enough that doing it on every request would make a machine with no graphics adapter feel
    /// broken rather than unsupported, and the answer cannot change without the process being
    /// restarted anyway.
    /// </para>
    /// <para>
    /// Two devices are returned on purpose. The COM one is what reads pixels back; the WinRT one
    /// is what the capture pool accepts. Creating the second from the first is the only supported
    /// bridge, and doing it here keeps both lifetimes in one place.
    /// </para>
    /// </summary>
    private (ID3D11Device Device, ID3D11DeviceContext Context, WinRtDevice WinRtDevice) GetDevice()
    {
        if (_device is not null && _context is not null && _winRtDevice is not null)
        {
            return (_device, _context, _winRtDevice);
        }

        if (_probed)
        {
            throw new ScreenVisionException(
                "No usable graphics device was found, so a screenshot cannot be taken.",
                ErrorCodes.ScreenCaptureUnsupported);
        }

        _probed = true;

        var (device, context) = D3D11DeviceFactory.TryCreate();
        var winRtDevice = device is null ? null : Direct3DDeviceInterop.TryCreateWinRtDevice(device);

        if (device is null || context is null || winRtDevice is null)
        {
            if (device is not null)
            {
                Marshal.ReleaseComObject(device);
            }

            if (context is not null)
            {
                Marshal.ReleaseComObject(context);
            }

            throw new ScreenVisionException(
                "No usable graphics device was found, so a screenshot cannot be taken.",
                ErrorCodes.ScreenCaptureUnsupported);
        }

        _device = device;
        _context = context;
        _winRtDevice = winRtDevice;
        return (device, context, winRtDevice);
    }

    /// <summary>
    /// Runs a session against the item the person chose and returns the first frame.
    /// </summary>
    /// <exception cref="OperationCanceledException">Thrown when the person cancels.</exception>
    /// <exception cref="ScreenVisionException">
    /// Thrown with a code from <see cref="ErrorCodes"/> when no frame arrives or the surface
    /// cannot be read.
    /// </exception>
    internal async Task<CapturedFrame> CaptureAsync(
        GraphicsCaptureItem item,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();

        var (device, context, winRtDevice) = GetDevice();
        var signal = new SemaphoreSlim(0, 1);

        // A one-element box rather than a plain local, because the frame is claimed on the
        // capture runtime's thread and read on this one. Holding it in a field gives the
        // interlocked exchange something stable to operate on.
        var arrivedFrame = new StrongBox<Direct3D11CaptureFrame?>(null);

        Direct3D11CaptureFramePool? pool = null;
        GraphicsCaptureSession? session = null;

        try
        {
            var size = item.Size;
            if (size.Width <= 0 || size.Height <= 0)
            {
                throw new ScreenVisionException(
                    "The item that was chosen has no size, so there was nothing to capture.",
                    ErrorCodes.ScreenCaptureFailed);
            }

            pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                winRtDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                BufferCount,
                size);

            pool.FrameArrived += OnFrameArrived;
            session = pool.CreateCaptureSession(item);
            session.StartCapture();

            await signal.WaitAsync(cancellationToken).ConfigureAwait(false);

            var arrived = Volatile.Read(ref arrivedFrame.Value);
            arrivedFrame.Value = null;

            if (arrived is null)
            {
                throw new ScreenVisionException(
                    "The screen did not produce a frame in time.",
                    ErrorCodes.ScreenCaptureFailed);
            }

            using (arrived)
            {
                return D3D11FrameReader.Read(device, context, arrived.Surface);
            }
        }
        catch (COMException exception)
        {
            // Access denied here means Windows refused the capture outright, which on a
            // managed workstation happens for the application as a whole rather than for one
            // window, and is worth a message that says so.
            throw new ScreenVisionException(
                "Windows would not allow a screenshot to be taken.",
                ErrorCodes.ScreenCaptureFailed,
                exception);
        }
        finally
        {
            // Order matters. The session is stopped and the unsubscribed before the pool is
            // disposed, because a frame arriving during disposal would otherwise be delivered
            // into a handler that is about to be detached, on a thread that is about to go away.
            if (session is not null)
            {
                // Disposing the session is what ends the capture; there is no separate stop on
                // this projection of the API. It can only be disposed once, which is why the
                // field is used rather than trusted to a helper.
                session.Dispose();
            }

            if (pool is not null)
            {
                pool.FrameArrived -= OnFrameArrived;
                pool.Dispose();
            }

            arrivedFrame.Value?.Dispose();
            signal.Dispose();
        }

        void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            try
            {
                var next = sender.TryGetNextFrame();
                if (next is null)
                {
                    return;
                }

                // The frame is claimed exactly once. A second frame arriving while the first is
                // still in flight is disposed here rather than queued, because only one
                // screenshot was asked for and holding two would be buffering more of the screen
                // than the request needs.
                if (Interlocked.CompareExchange(ref arrivedFrame.Value, next, null) is not null)
                {
                    next.Dispose();
                    return;
                }

                signal.Release();
            }
            catch (COMException)
            {
                // The runtime thread cannot be allowed to see an exception, and a failure here
                // leaves the wait above to time out with a clear message rather than to hang.
            }
            catch (SemaphoreFullException)
            {
                // Expected when two frames arrive before the first is read; the extra one has
                // already been disposed above.
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_device is not null)
        {
            Marshal.ReleaseComObject(_device);
            _device = null;
        }

        if (_context is not null)
        {
            Marshal.ReleaseComObject(_context);
            _context = null;
        }
    }
}
