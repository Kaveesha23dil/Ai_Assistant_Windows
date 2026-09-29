using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace WindowsAIAssistant.Infrastructure.Vision.Interop;

/// <summary>
/// Turns a raw Direct3D 11 device into the WinRT one that the capture pool insists on.
/// <para>
/// Windows Graphics Capture talks in WinRT types, while a device created through
/// <c>D3D11CreateDevice</c> is a plain COM object. The bridge between them is one exported
/// function that wraps a DXGI device as an <c>IDirect3DDevice</c>, and it is declared here
/// rather than reached through a helper package because it is the only thing this application
/// would ever use such a package for.
/// </para>
/// <para>
/// Everything below is a foot-gun the type system cannot catch, so the references are released
/// in a single finally block and a failure returns <see langword="null"/> rather than an
/// exception: a machine where this does not work is a machine that cannot share its screen, which
/// is a supported outcome and not a fault.
/// </para>
/// </summary>
internal static class Direct3DDeviceInterop
{
    /// <summary>The interface identifier of <c>IDXGIDevice</c>, which every D3D11 device also implements.</summary>
    private static readonly Guid IDXGIDeviceGuid = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

    /// <summary>
    /// Wraps a Direct3D 11 device as a WinRT device, or returns <see langword="null"/> when the
    /// bridge is unavailable on this machine.
    /// </summary>
    internal static IDirect3DDevice? TryCreateWinRtDevice(ID3D11Device device)
    {
        ArgumentNullException.ThrowIfNull(device);

        IntPtr unknown = IntPtr.Zero;
        IntPtr dxgiDevice = IntPtr.Zero;
        IntPtr inspectable = IntPtr.Zero;

        try
        {
            unknown = Marshal.GetIUnknownForObject(device);
            if (unknown == IntPtr.Zero)
            {
                return null;
            }

            var iid = IDXGIDeviceGuid;
            if (Marshal.QueryInterface(unknown, in iid, out dxgiDevice) < 0 || dxgiDevice == IntPtr.Zero)
            {
                return null;
            }

            if (CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out inspectable) < 0 || inspectable == IntPtr.Zero)
            {
                return null;
            }

            return MarshalInspectable<IDirect3DDevice>.FromAbi(inspectable);
        }
        catch (Exception)
        {
            // A COM failure here is exactly the "this machine cannot do it" case, so it is
            // reported as an absent device rather than thrown at a caller mid-capture.
            return null;
        }
        finally
        {
            if (inspectable != IntPtr.Zero)
            {
                Marshal.Release(inspectable);
            }

            if (dxgiDevice != IntPtr.Zero)
            {
                Marshal.Release(dxgiDevice);
            }

            if (unknown != IntPtr.Zero)
            {
                Marshal.Release(unknown);
            }
        }
    }

    /// <summary>
    /// The one export from <c>d3d11.dll</c> that produces a WinRT surface wrapper.
    /// </summary>
    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(
        IntPtr dxgiDevice,
        out IntPtr graphicsDevice);
}
