using System.Runtime.InteropServices;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Infrastructure.Vision.Interop;
using WinRT;
using ID3D11Device = WindowsAIAssistant.Infrastructure.Vision.Interop.ID3D11Device;
using ID3D11DeviceContext = WindowsAIAssistant.Infrastructure.Vision.Interop.ID3D11DeviceContext;
using ID3D11Texture2D = WindowsAIAssistant.Infrastructure.Vision.Interop.ID3D11Texture2D;

// The WinRT namespace, named absolutely. This project has a WindowsAIAssistant.Infrastructure.Windows
// namespace of its own, and inside this namespace chain a plain "Windows.Graphics" binds to that
// one instead of the platform's — which fails in a way that reads like a missing reference rather
// than a shadowed name.
//
// The type is named IDirect3DSurface here rather than Direct3DSurface because that is how
// C#/WinRT projects the runtime class: the projection puts an I in front whenever the class name
// would otherwise collide with the interface the class implements.
using IDirect3DSurface = global::Windows.Graphics.DirectX.Direct3D11.IDirect3DSurface;

namespace WindowsAIAssistant.Infrastructure.Vision.Capture;

/// <summary>
/// One frame read off the GPU into system memory.
/// <para>
/// Disposable, and that is not ceremony. A 4K frame is roughly thirty megabytes of somebody's
/// screen, and the capture runtime is designed to be handed its buffer back promptly. Erasing
/// the buffer on disposal rather than merely dropping the reference means a screenshot of
/// something sensitive is not still readable in freed memory for the rest of the session.
/// </para>
/// </summary>
public sealed class CapturedFrame : IDisposable
{
    private byte[]? _pixels;

    internal CapturedFrame(byte[] pixels, int width, int height, int rowPitch)
    {
        _pixels = pixels;
        Width = width;
        Height = height;
        RowPitch = rowPitch;
    }

    /// <summary>Gets the frame width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the frame height in pixels.</summary>
    public int Height { get; }

    /// <summary>
    /// Gets the number of bytes between the start of one row and the next.
    /// <para>
    /// Larger than <c>Width * 4</c>, because Direct3D requires each row to start on a 256-byte
    /// boundary. Every arithmetic operation on these pixels has to use this rather than the
    /// packed width, and <see cref="ExtractRegion"/> is the only place that does.
    /// </para>
    /// </summary>
    public int RowPitch { get; }

    /// <summary>Gets a value indicating whether the buffer has been erased.</summary>
    public bool IsReleased => _pixels is null;

    /// <summary>Gets the pixels. Throws once disposed.</summary>
    public ReadOnlySpan<byte> Pixels =>
        _pixels is null ? throw new ObjectDisposedException(nameof(CapturedFrame)) : _pixels;

    /// <summary>
    /// Copies a rectangle out of the frame, undoing the row padding.
    /// <para>
    /// A method rather than an index calculation at the call site, because the padding is the
    /// kind of detail that is right in one place and wrong in three. A 1919-pixel-wide frame
    /// has a row pitch of 7680, not 7676, and treating the buffer as tightly packed shears every
    /// row by a different amount — which looks like image corruption rather than like a bug.
    /// </para>
    /// </summary>
    /// <exception cref="ImageProcessingException">
    /// Thrown when the rectangle is not inside the frame. Refused rather than clipped, so that a
    /// highlight drawn over the wrong thing cannot be answered as though it were the right one.
    /// </exception>
    public byte[] ExtractRegion(int x, int y, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (x + width > Width || y + height > Height)
        {
            throw new ImageProcessingException(
                "That part of the screenshot is outside the captured image.",
                ErrorCodes.ScreenRegionOutsideFrame);
        }

        var source = Pixels;
        var destination = new byte[width * height * 4];
        var rowBytes = width * 4;

        for (var row = 0; row < height; row++)
        {
            var sourceStart = ((y + row) * RowPitch) + (x * 4);
            source.Slice(sourceStart, rowBytes).CopyTo(destination.AsSpan(row * rowBytes, rowBytes));
        }

        return destination;
    }

    /// <summary>
    /// Reports whether the frame is blank, which is how a protected window appears.
    /// <para>
    /// Windows hands over a frame for content it will not let the application read, and that
    /// frame is solid black or solid transparent. Analysing it would produce a confident
    /// description of nothing, so it is recognised here and reported as protected rather than
    /// passed on. The check samples rather than scans: it is looking for a frame with no
    /// variation at all, not for one that happens to be dark, and a dark screenshot of a
    /// dark room is perfectly good.
    /// </para>
    /// </summary>
    public bool AppearsBlank()
    {
        if (IsReleased)
        {
            return true;
        }

        var pixels = Pixels;
        var first = pixels[0];
        var second = pixels[1];
        var third = pixels[2];

        // A stride of a few thousand bytes keeps this cheap on a large display: a frame that
        // differs anywhere in a few thousand samples is not blank, and one that matches
        // throughout is blank enough to report.
        const int Stride = 997 * 4;

        for (var offset = Stride; offset + 3 < pixels.Length; offset += Stride)
        {
            if (pixels[offset] != first ||
                pixels[offset + 1] != second ||
                pixels[offset + 2] != third)
            {
                return false;
            }
        }

        return first == 0 && second == 0 && third == 0;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_pixels is null)
        {
            return;
        }

        Array.Clear(_pixels);
        _pixels = null;
    }
}

/// <summary>
/// Copies a Direct3D texture into system memory.
/// <para>
/// A GPU texture cannot be read directly, so the frame is copied into a staging texture — one
/// the CPU is allowed to map — and then read row by row. This is the only place in the
/// application that does that copy, so the lifetime of the staging texture, the map, and the
/// unreferenced COM pointers are all in one method rather than spread across the capture path.
/// </para>
/// </summary>
internal static class D3D11FrameReader
{
    private static readonly Guid ID3D11Texture2DGuid = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    private static readonly Guid IDirect3DDxgiInterfaceAccessGuid = new("a9b3d012-3df2-4ee3-b8d1-8695f457d3c1");

    /// <summary>
    /// Reads a capture surface into a managed buffer.
    /// </summary>
    /// <exception cref="ScreenVisionException">
    /// Thrown with a code from <see cref="ErrorCodes"/> when the surface cannot be reached or
    /// the copy fails. Never for a cancellation.
    /// </exception>
    internal static CapturedFrame Read(ID3D11Device device, ID3D11DeviceContext context, IDirect3DSurface surface)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(surface);

        var texture = GetTexture(surface);
        if (texture is null)
        {
            throw new ScreenVisionException(
                "The captured frame could not be read from the graphics device.",
                ErrorCodes.ScreenImageInvalid);
        }

        ID3D11Texture2D? staging = null;

        try
        {
            texture.GetDesc(out var description);

            var width = (int)description.Width;
            var height = (int)description.Height;

            if (width <= 0 || height <= 0)
            {
                throw new ScreenVisionException(
                    "The captured frame had no pixels in it.",
                    ErrorCodes.ScreenCaptureEmpty);
            }

            staging = CreateStagingTexture(device, description);
            if (staging is null)
            {
                throw new ScreenVisionException(
                    "A copy of the captured frame could not be made.",
                    ErrorCodes.ScreenImageInvalid);
            }

            // The destination first, as the C interface declares it. Reversed, the copy
            // succeeds and quietly produces a frame of whatever was in the staging texture,
            // which is a plausible-looking screenshot of nothing.
            context.Slot44_CopyResource(staging, texture);
            context.Slot11_Map(staging, 0, MapModes.Read, 0, out var mapped);

            try
            {
                return CopyRows(mapped, width, height);
            }
            finally
            {
                // Released before the staging texture itself, because leaving a resource mapped
                // makes the driver hold onto it and the next capture can fail for a reason that
                // has nothing to do with the next capture.
                context.Slot12_Unmap(staging, 0);
            }
        }
        catch (ScreenVisionException)
        {
            throw;
        }
        catch (COMException exception)
        {
            throw new ScreenVisionException(
                "The captured frame could not be read from the graphics device.",
                ErrorCodes.ScreenImageInvalid,
                exception);
        }
        finally
        {
            Release(staging);
            Release(texture);
        }
    }

    /// <summary>
    /// Releases a COM interface this application owns.
    /// <para>
    /// A projected Direct3D interface is an ordinary runtime callable wrapper, so it is released
    /// by the wrapper's own method rather than by the raw-pointer one. Calling the raw overload
    /// on a wrapper is the kind of mistake that compiles against one overload set and corrupts
    /// the heap against another.
    /// </para>
    /// </summary>
    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.ReleaseComObject(comObject);
        }
    }

    /// <summary>
    /// Asks a capture surface for the Direct3D texture behind it.
    /// <para>
    /// Every <c>IDirect3DSurface</c> implements <c>IDirect3DDxgiInterfaceAccess</c>, but the
    /// projected WinRT type does not expose it, so the surface is taken to its unmanaged identity
    /// and the interface is asked for by identifier. This is done with explicit reference
    /// counting rather than a cast helper, because the two objects involved are a WinRT one and a
    /// classic COM one and a single helper that covered both would be doing two different things
    /// behind one name.
    /// </para>
    /// <para>
    /// The returned texture is a new reference and is released by the caller. Every pointer taken
    /// here is released on the way out, including on the paths that return nothing.
    /// </para>
    /// </summary>
    private static ID3D11Texture2D? GetTexture(IDirect3DSurface surface)
    {
        IntPtr surfacePointer = IntPtr.Zero;
        IntPtr accessPointer = IntPtr.Zero;
        IntPtr texturePointer = IntPtr.Zero;

        try
        {
            surfacePointer = MarshalInspectable<object>.FromManaged(surface);

            var accessIid = IDirect3DDxgiInterfaceAccessGuid;
            if (Marshal.QueryInterface(surfacePointer, in accessIid, out accessPointer) < 0 ||
                accessPointer == IntPtr.Zero)
            {
                return null;
            }

            var access = (IDirect3DDxgiInterfaceAccess)Marshal.GetObjectForIUnknown(accessPointer);

            var textureIid = ID3D11Texture2DGuid;
            access.GetInterface(ref textureIid, out texturePointer);

            if (texturePointer == IntPtr.Zero)
            {
                return null;
            }

            return (ID3D11Texture2D)Marshal.GetObjectForIUnknown(texturePointer);
        }
        catch (COMException)
        {
            return null;
        }
        catch (InvalidCastException)
        {
            return null;
        }
        finally
        {
            // The interfaced texture keeps its own reference, so the raw pointer taken from it is
            // released here rather than handed on: two owners of one reference is a double
            // release waiting for an unlucky day.
            if (texturePointer != IntPtr.Zero)
            {
                Marshal.Release(texturePointer);
            }

            if (accessPointer != IntPtr.Zero)
            {
                Marshal.Release(accessPointer);
            }

            if (surfacePointer != IntPtr.Zero)
            {
                Marshal.Release(surfacePointer);
            }
        }
    }

    /// <summary>
    /// Creates a CPU-readable texture of the same size and format as the frame.
    /// </summary>
    private static ID3D11Texture2D? CreateStagingTexture(ID3D11Device device, Texture2DDescription description)
    {
        var staging = new Texture2DDescription
        {
            Width = description.Width,
            Height = description.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = description.Format,
            SampleCount = 1,
            SampleQuality = 0,
            Usage = (uint)ResourceUsage.Staging,
            BindFlags = (uint)BindFlags.None,
            CpuAccessFlags = (uint)CpuAccessFlags.Read,
            MiscFlags = 0,
        };

        // The description is passed as an unmanaged block rather than a blittable struct so
        // that the layout sent to the driver is exactly the header's, with no chance of the
        // managed padding of a differently-shaped struct deciding where a field lands.
        var size = Marshal.SizeOf<Texture2DDescription>();
        var pointer = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(staging, pointer, fDeleteOld: false);
            device.CreateTexture2D(pointer, IntPtr.Zero, out var texture);
            return texture;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    /// <summary>
    /// Copies the mapped rows into a tightly packed managed buffer.
    /// </summary>
    private static CapturedFrame CopyRows(MappedSubresource mapped, int width, int height)
    {
        var rowPitch = (int)mapped.RowPitch;
        var rowBytes = width * 4;
        var pixels = new byte[rowBytes * height];

        for (var row = 0; row < height; row++)
        {
            Marshal.Copy(
                mapped.Data + (row * rowPitch),
                pixels,
                row * rowBytes,
                rowBytes);
        }

        return new CapturedFrame(pixels, width, height, rowPitch);
    }
}
