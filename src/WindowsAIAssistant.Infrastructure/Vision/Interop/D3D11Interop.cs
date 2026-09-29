using System.Runtime.InteropServices;

namespace WindowsAIAssistant.Infrastructure.Vision.Interop;

/// <summary>
/// The Direct3D 11 entry points needed to read a captured frame back to system memory.
/// <para>
/// Declared by hand rather than through a graphics library. This feature needs three COM
/// methods and a device creation call, and taking a dependency on a full graphics binding to
/// obtain those would add a package whose only content this application would ever use is
/// these declarations.
/// </para>
/// <para>
/// The method order in each interface is load-bearing and is not negotiable: a COM call is a
/// jump through a vtable, so a single method in the wrong place turns a call into a jump to an
/// unrelated function. The order below was taken from <c>d3d11.h</c>, and the methods this
/// application does not call are declared as named empty slots with their real names in the
/// documentation, so a reader can check the sequence without reading the header.
/// </para>
/// <para>
/// Unused slots are <c>void</c> with no parameters. That is deliberate: they are never invoked,
/// and giving them plausible-looking signatures would only invite somebody to call one and
/// discover the problem at runtime rather than at compile time.
/// </para>
/// </summary>
[ComImport]
[Guid("db6f6ddb-ac77-4e88-8253-819df9bbf140")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ID3D11Device
{
    /// <summary>Slot 0 — CreateBuffer.</summary>
    void Slot00_CreateBuffer();

    /// <summary>Slot 1 — CreateTexture1D.</summary>
    void Slot01_CreateTexture1D();

    /// <summary>Slot 2 — CreateTexture2D. The only method this application calls.</summary>
    void CreateTexture2D(IntPtr description, IntPtr initialData, out ID3D11Texture2D texture);

    // Slots 3 and onwards — CreateTexture3D, CreateShaderResourceView, and the rest of the
    // device surface, none of which a capture readback needs.
}

/// <summary>
/// The immediate device context, as a pointer rather than an interface.
/// <para>
/// Declared as an opaque pointer because the only three methods used are at vtable positions
/// 11, 12 and 44, and enumerating forty-four empty slots to reach the last of them would be
/// unreadable and easy to get wrong. <see cref="Direct3DContext"/> calls through the slots by
/// index instead, so the positions are named once, in one place, with the header method each
/// one is.
/// </para>
/// </summary>
[ComImport]
[Guid("c0bfa96c-e089-44fb-8eaf-26f8796190da")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ID3D11DeviceContext
{
    /// <summary>Slot 0 — GetDevice.</summary>
    void Slot00();

    /// <summary>Slot 1 — GetPrivateData.</summary>
    void Slot01();

    /// <summary>Slot 2 — SetPrivateData.</summary>
    void Slot02();

    /// <summary>Slot 3 — SetPrivateDataInterface.</summary>
    void Slot03();

    /// <summary>Slot 4 — VSSetConstantBuffers.</summary>
    void Slot04();

    /// <summary>Slot 5 — PSSetShaderResources.</summary>
    void Slot05();

    /// <summary>Slot 6 — PSSetShader.</summary>
    void Slot06();

    /// <summary>Slot 7 — PSSetSamplers.</summary>
    void Slot07();

    /// <summary>Slot 8 — VSSetShader.</summary>
    void Slot08();

    /// <summary>Slot 9 — DrawIndexed.</summary>
    void Slot09();

    /// <summary>Slot 10 — Draw.</summary>
    void Slot10();

    /// <summary>Slot 11 — Map. The first of the three methods this application calls.</summary>
    void Slot11_Map(ID3D11Texture2D resource, int subresource, int mapType, int mapFlags, out MappedSubresource mapped);

    /// <summary>Slot 12 — Unmap.</summary>
    void Slot12_Unmap(ID3D11Texture2D resource, int subresource);

    /// <summary>Slot 13 — PSSetConstantBuffers.</summary>
    void Slot13();

    /// <summary>Slot 14 — IASetInputLayout.</summary>
    void Slot14();

    /// <summary>Slot 15 — IASetVertexBuffers.</summary>
    void Slot15();

    /// <summary>Slot 16 — IASetIndexBuffer.</summary>
    void Slot16();

    /// <summary>Slot 17 — DrawIndexedInstanced.</summary>
    void Slot17();

    /// <summary>Slot 18 — DrawInstanced.</summary>
    void Slot18();

    /// <summary>Slot 19 — GSSetConstantBuffers.</summary>
    void Slot19();

    /// <summary>Slot 20 — GSSetShader.</summary>
    void Slot20();

    /// <summary>Slot 21 — IASetPrimitiveTopology.</summary>
    void Slot21();

    /// <summary>Slot 22 — VSSetShaderResources.</summary>
    void Slot22();

    /// <summary>Slot 23 — VSSetSamplers.</summary>
    void Slot23();

    /// <summary>Slot 24 — Begin.</summary>
    void Slot24();

    /// <summary>Slot 25 — End.</summary>
    void Slot25();

    /// <summary>Slot 26 — GetData.</summary>
    void Slot26();

    /// <summary>Slot 27 — SetPredication.</summary>
    void Slot27();

    /// <summary>Slot 28 — GSSetShaderResources.</summary>
    void Slot28();

    /// <summary>Slot 29 — GSSetSamplers.</summary>
    void Slot29();

    /// <summary>Slot 30 — OMSetRenderTargets.</summary>
    void Slot30();

    /// <summary>Slot 31 — OMSetRenderTargetsAndUnorderedAccessViews.</summary>
    void Slot31();

    /// <summary>Slot 32 — OMSetBlendState.</summary>
    void Slot32();

    /// <summary>Slot 33 — OMSetDepthStencilState.</summary>
    void Slot33();

    /// <summary>Slot 34 — SOSetTargets.</summary>
    void Slot34();

    /// <summary>Slot 35 — DrawAuto.</summary>
    void Slot35();

    /// <summary>Slot 36 — DrawIndexedInstancedIndirect.</summary>
    void Slot36();

    /// <summary>Slot 37 — DrawInstancedIndirect.</summary>
    void Slot37();

    /// <summary>Slot 38 — Dispatch.</summary>
    void Slot38();

    /// <summary>Slot 39 — DispatchIndirect.</summary>
    void Slot39();

    /// <summary>Slot 40 — RSSetState.</summary>
    void Slot40();

    /// <summary>Slot 41 — RSSetViewports.</summary>
    void Slot41();

    /// <summary>Slot 42 — RSSetScissorRects.</summary>
    void Slot42();

    /// <summary>Slot 43 — CopySubresourceRegion.</summary>
    void Slot43();

    /// <summary>Slot 44 — CopyResource. The third and last of the methods this application calls.</summary>
    void Slot44_CopyResource(ID3D11Texture2D destination, ID3D11Texture2D source);

    /// <summary>Slot 45 — UpdateSubresource.</summary>
    void Slot45();
}

[ComImport]
[Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ID3D11Texture2D
{
    /// <summary>Slot 0 — GetDevice.</summary>
    void Slot00();

    /// <summary>Slot 1 — GetPrivateData.</summary>
    void Slot01();

    /// <summary>Slot 2 — SetPrivateData.</summary>
    void Slot02();

    /// <summary>Slot 3 — SetPrivateDataInterface.</summary>
    void Slot03();

    /// <summary>Slot 4 — GetDesc. The only method this application calls.</summary>
    void GetDesc(out Texture2DDescription description);

    /// <summary>Slot 5 — GetType.</summary>
    void Slot05();

    /// <summary>Slot 6 — SetEvictionPriority.</summary>
    void Slot06();

    /// <summary>Slot 7 — GetEvictionPriority.</summary>
    void Slot07();

    /// <summary>Slot 8 — GetResource.</summary>
    void Slot08();

    /// <summary>Slot 9 — GetLevelDesc.</summary>
    void Slot09();

    /// <summary>Slot 10 — GetSurfaceLevelData.</summary>
    void Slot10();

    /// <summary>Slot 11 — Map.</summary>
    void Slot11();

    /// <summary>Slot 12 — Unmap.</summary>
    void Slot12();
}

/// <summary>The layout of a texture, read back with <c>GetDesc</c>.</summary>
/// <remarks>
/// The sample description is two fields, not one. It is the one place this struct differs from
/// the shape a reader expects from <c>d3d11.h</c>'s nested <c>DXGI_SAMPLE_DESC</c>, and getting
/// it wrong does not fail loudly: every field after it lands four bytes early, so the usage and
/// CPU-access flags a staging texture is created with are read from the wrong slots and the
/// texture comes back unreadable.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct Texture2DDescription
{
    public uint Width;
    public uint Height;
    public uint MipLevels;
    public uint ArraySize;
    public uint Format;
    public uint SampleCount;
    public uint SampleQuality;
    public uint Usage;
    public uint BindFlags;
    public uint CpuAccessFlags;
    public uint MiscFlags;
}

/// <summary>A mapped staging texture: where the bytes are and how far apart the rows are.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MappedSubresource
{
    public IntPtr Data;
    public uint RowPitch;
    public uint DepthPitch;
}

/// <summary>The three <c>Map</c> modes, which are the only ones a readback uses.</summary>
internal static class MapModes
{
    public const int Read = 1;
    public const int Write = 2;
}

/// <summary>The two resource usages a readback distinguishes.</summary>
[Flags]
internal enum ResourceUsage : uint
{
    Default = 0,
    Immutable = 1,
    Dynamic = 2,
    Staging = 4,
    ReadBack = 6,
}

/// <summary>Binding flags, of which a staging texture uses none.</summary>
[Flags]
internal enum BindFlags : uint
{
    None = 0,
    ShaderResource = 0x40,
    RenderTarget = 0x100,
    DepthStencil = 0x400,
}

/// <summary>CPU access flags, which must agree with <see cref="ResourceUsage.Staging"/>.</summary>
[Flags]
internal enum CpuAccessFlags : uint
{
    None = 0,
    Read = 0x1,
    Write = 0x2,
}

/// <summary>
/// Reaches the Direct3D texture behind a WinRT capture surface.
/// <para>
/// The one piece of interop with no SDK header on this machine, and therefore the one place
/// where a wrong interface identifier would fail as an unhelpful <c>QueryInterface</c> error
/// rather than as a compile error. The identifier and the single-method layout are the ones
/// Microsoft documents for <c>IDirect3DDxgiInterfaceAccess</c>, which every
/// <c>IDirect3DSurface</c> implements.
/// </para>
/// </summary>
[ComImport]
[Guid("a9b3d012-3df2-4ee3-b8d1-8695f457d3c1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDirect3DDxgiInterfaceAccess
{
    void GetInterface([In] ref Guid interfaceId, out IntPtr result);
}

/// <summary>
/// Creates a D3D11 device and its immediate context.
/// <para>
/// A single P/Invoke rather than a full binding, because device creation is one function with
/// six out parameters and nothing else in the API is needed here. The software rasteriser is a
/// real fallback rather than a nicety: a remote desktop session or a virtual machine with no 3D
/// adapter has no hardware device to create, and reporting "this computer cannot take a
/// screenshot" on a machine that plainly could is a worse answer than capturing a little more
/// slowly.
/// </para>
/// </summary>
internal static class D3D11DeviceFactory
{
    private const int SdkVersion = 7;
    private const int DriverTypeHardware = 1;
    private const int DriverTypeReference = 4;
    private const int DriverTypeWarp = 8;

    private static readonly int[] FeatureLevels =
    [
        0xb100, // 11_1
        0xb000, // 11_0
        0xa100, // 10_1
        0xa000, // 10_0
        0x9100, // 9_1
        0x9300, // 9_3
    ];

    /// <summary>
    /// Creates a device, or returns null when no adapter of any kind can be used.
    /// <para>
    /// Returns null rather than raising, so a caller can report a specific, user-safe reason —
    /// "this computer has no usable graphics device" — instead of surfacing a native error code.
    /// </para>
    /// </summary>
    internal static (ID3D11Device? Device, ID3D11DeviceContext? Context) TryCreate()
    {
        foreach (var driverType in new[] { DriverTypeHardware, DriverTypeWarp, DriverTypeReference })
        {
            var result = TryCreateFor(driverType);
            if (result.Device is not null && result.Context is not null)
            {
                return result;
            }
        }

        return (null, null);
    }

    private static (ID3D11Device? Device, ID3D11DeviceContext? Context) TryCreateFor(int driverType)
    {
        try
        {
            var hr = D3D11CreateDevice(
                IntPtr.Zero,
                driverType,
                IntPtr.Zero,
                0,
                FeatureLevels,
                (uint)FeatureLevels.Length,
                SdkVersion,
                out var device,
                out _,
                out var context);

            return hr >= 0 ? (device, context) : (null, null);
        }
        catch (DllNotFoundException)
        {
            return (null, null);
        }
        catch (EntryPointNotFoundException)
        {
            return (null, null);
        }
    }

    [DllImport("d3d11.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int D3D11CreateDevice(
        IntPtr adapter,
        int driverType,
        IntPtr software,
        uint flags,
        int[] featureLevels,
        uint featureLevelCount,
        uint sdkVersion,
        out ID3D11Device device,
        out uint featureLevel,
        out ID3D11DeviceContext immediateContext);
}
