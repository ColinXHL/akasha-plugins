using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;
using Device = SharpDX.Direct3D11.Device;

namespace AkashaAutomation.Core.Capture;

internal static class WindowsGraphicsCaptureInterop
{
    private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid Direct3DTexture2DGuid = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow([In] nint window, [In] ref Guid iid);

        nint CreateForMonitor([In] nint monitor, [In] ref Guid iid);
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        nint GetInterface([In] ref Guid iid);
    }

    [DllImport("d3d11.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern uint CreateDirect3D11DeviceFromDXGIDevice(
        nint dxgiDevice,
        out nint graphicsDevice);

    internal static GraphicsCaptureItem CreateItemForWindow(nint window)
    {
        var factory = WinrtActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interop = factory.AsInterface<IGraphicsCaptureItemInterop>();
        var itemGuid = GraphicsCaptureItemGuid;
        var pointer = interop.CreateForWindow(window, ref itemGuid);
        return GraphicsCaptureItem.FromAbi(pointer);
    }

    internal static IDirect3DDevice CreateWinRtDevice(Device device)
    {
        using var dxgiDevice = device.QueryInterface<SharpDX.DXGI.Device3>();
        var result = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var pointer);
        if (result != 0)
        {
            Marshal.ThrowExceptionForHR(unchecked((int)result));
        }

        try
        {
            return MarshalInterface<IDirect3DDevice>.FromAbi(pointer);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    internal static Texture2D CreateTexture(IDirect3DSurface surface)
    {
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        var textureGuid = Direct3DTexture2DGuid;
        var pointer = access.GetInterface(ref textureGuid);
        return new Texture2D(pointer);
    }

#pragma warning disable CS0169, CS0649
    [Guid("00000035-0000-0000-C000-000000000046")]
    private unsafe struct ActivationFactoryVtable
    {
        internal IInspectable.Vftbl InspectableVtable;
        private void* _activateInstance;
    }
#pragma warning restore CS0169, CS0649

    private static class WinrtActivationFactory
    {
        private static readonly Dictionary<string, ObjectReference<ActivationFactoryVtable>> Cache = [];
        private static readonly object Gate = new();
        private static nint _mtaCookie;

        [DllImport("api-ms-win-core-com-l1-1-0.dll")]
        private static extern unsafe int CoIncrementMTAUsage(nint* cookie);

        [DllImport("api-ms-win-core-winrt-l1-1-0.dll")]
        private static extern unsafe int RoGetActivationFactory(
            nint runtimeClassId,
            ref Guid iid,
            nint* factory);

        internal static ObjectReference<ActivationFactoryVtable> Get(string runtimeClassId)
        {
            lock (Gate)
            {
                if (Cache.TryGetValue(runtimeClassId, out var cached))
                {
                    Resurrect(cached);
                    return cached;
                }

                var marshaled = MarshalString.CreateMarshaler(runtimeClassId);
                try
                {
                    var pointer = GetActivationFactory(MarshalString.GetAbi(marshaled));
#pragma warning disable CS0618
                    var created = ObjectReference<ActivationFactoryVtable>.Attach(ref pointer);
#pragma warning restore CS0618
                    Cache.Add(runtimeClassId, created);
                    return created;
                }
                finally
                {
                    marshaled.Dispose();
                }
            }
        }

        private static unsafe nint GetActivationFactory(nint runtimeClassId)
        {
            if (_mtaCookie == nint.Zero)
            {
                nint cookie;
                Marshal.ThrowExceptionForHR(CoIncrementMTAUsage(&cookie));
                _mtaCookie = cookie;
            }

            var iid = typeof(ActivationFactoryVtable).GUID;
            nint pointer;
            var result = RoGetActivationFactory(runtimeClassId, ref iid, &pointer);
            if (result != 0)
            {
                throw new Win32Exception(result);
            }

            return pointer;
        }

        private static void Resurrect(IObjectReference objectReference)
        {
            var field = objectReference.GetType().GetField("disposed", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field?.GetValue(objectReference) is true)
            {
                field.SetValue(objectReference, false);
                GC.ReRegisterForFinalize(objectReference);
            }
        }
    }
}
