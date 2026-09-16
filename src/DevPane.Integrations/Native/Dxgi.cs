using System.Runtime.InteropServices;

namespace DevPane.Integrations.Native;

/// <summary>
/// Finds the machine's main GPU through DXGI, for its name, memory size and LUID (which PDH uses in counter names).
/// </summary>
internal static unsafe partial class Dxgi
{
    private const uint AdapterFlagSoftware = 2;

    // Vtable slots: IUnknown (0-2), IDXGIObject (3-6), IDXGIFactory (7-11), then IDXGIFactory1.EnumAdapters1.
    private const int EnumAdapters1Slot = 12;

    // IUnknown (0-2), IDXGIObject (3-6), IDXGIAdapter (7-9), then IDXGIAdapter1.GetDesc1.
    private const int GetDesc1Slot = 10;
    private const int ReleaseSlot = 2;

    private static readonly Guid IidDxgiFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    public readonly record struct Adapter(string Name, uint LuidLowPart, int LuidHighPart, ulong DedicatedMemory, ulong SharedMemory);

    /// <summary>The hardware GPU with the most dedicated memory, or null when there is none.</summary>
    public static Adapter? FindPrimaryGpu()
    {
        Guid iid = IidDxgiFactory1;
        if (CreateDXGIFactory1(&iid, out nint factory) < 0)
        {
            return null;
        }

        try
        {
            var enumAdapters1 = (delegate* unmanaged<nint, uint, nint*, int>)(*(nint**)factory)[EnumAdapters1Slot];
            Adapter? best = null;

            for (uint index = 0; ; index++)
            {
                nint adapter;
                if (enumAdapters1(factory, index, &adapter) < 0)
                {
                    // DXGI_ERROR_NOT_FOUND marks the end of the list.
                    break;
                }

                try
                {
                    var getDesc1 = (delegate* unmanaged<nint, AdapterDesc1*, int>)(*(nint**)adapter)[GetDesc1Slot];
                    AdapterDesc1 desc;
                    if (getDesc1(adapter, &desc) < 0 || (desc.Flags & AdapterFlagSoftware) != 0)
                    {
                        continue;
                    }

                    ulong dedicated = desc.DedicatedVideoMemory;
                    if (best is null || dedicated > best.Value.DedicatedMemory)
                    {
                        best = new Adapter(new string(desc.Description), desc.AdapterLuidLowPart, desc.AdapterLuidHighPart,
                            dedicated, desc.SharedSystemMemory);
                    }
                }
                finally
                {
                    Release(adapter);
                }
            }

            return best;
        }
        finally
        {
            Release(factory);
        }
    }

    private static void Release(nint unknown) =>
        ((delegate* unmanaged<nint, uint>)(*(nint**)unknown)[ReleaseSlot])(unknown);

    // DXGI_ADAPTER_DESC1
    [StructLayout(LayoutKind.Sequential)]
    private struct AdapterDesc1
    {
        public fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint AdapterLuidLowPart;
        public int AdapterLuidHighPart;
        public uint Flags;
    }

    [LibraryImport("dxgi.dll")]
    private static partial int CreateDXGIFactory1(Guid* riid, out nint factory);
}
