using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MpGameDub.Windows;

public sealed record GpuAdapter(int Index, string Name, long DedicatedMemory, bool Software);

/// <summary>
/// Lista as placas na ordem do DXGI, a mesma que o DirectML usa para escolher o aparelho. Num Ryzen com vídeo
/// integrado mais uma placa dedicada, o índice 0 pode ser o integrado; por isso a voz escolhe a de mais memória.
/// </summary>
[SupportedOSPlatform("windows")]
public static partial class GpuAdapters
{
    public static IReadOnlyList<GpuAdapter> List()
    {
        var result = new List<GpuAdapter>();
        var iid = typeof(IDXGIFactory1).GUID;
        if (CreateDXGIFactory1(ref iid, out var factoryObject) < 0) return result;
        var factory = (IDXGIFactory1)factoryObject;
        try
        {
            for (uint i = 0; factory.EnumAdapters1(i, out var adapter) >= 0; i++)
            {
                try
                {
                    adapter.GetDesc1(out var desc);
                    result.Add(new GpuAdapter((int)i, desc.Description.TrimEnd('\0'), (long)desc.DedicatedVideoMemory, (desc.Flags & 2) != 0));
                }
                finally
                {
                    Marshal.ReleaseComObject(adapter);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }
        return result;
    }

    /// <summary>A placa física com mais memória dedicada, ou null se só houver o adaptador por software.</summary>
    public static GpuAdapter? Best() => List().Where(a => !a.Software).MaxBy(a => a.DedicatedMemory);

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object factory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }

    // Só os métodos usados, na ordem da vtable (IDXGIObject: 4 métodos; IDXGIFactory: 5; IDXGIFactory1: EnumAdapters1).
    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory1
    {
        void SetPrivateData();
        void SetPrivateDataInterface();
        void GetPrivateData();
        void GetParent();
        void EnumAdapters();
        void MakeWindowAssociation();
        void GetWindowAssociation();
        void CreateSwapChain();
        void CreateSoftwareAdapter();
        [PreserveSig] int EnumAdapters1(uint index, out IDXGIAdapter1 adapter);
    }

    // IDXGIObject: 4; IDXGIAdapter: EnumOutputs, GetDesc, CheckInterfaceSupport; IDXGIAdapter1: GetDesc1.
    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter1
    {
        void SetPrivateData();
        void SetPrivateDataInterface();
        void GetPrivateData();
        void GetParent();
        void EnumOutputs();
        void GetDesc();
        void CheckInterfaceSupport();
        void GetDesc1(out DxgiAdapterDesc1 desc);
    }
}
