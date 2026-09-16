using System.Runtime.InteropServices;

namespace DevPane.Integrations.SystemStats;

/// <summary>
/// Reads physical memory usage.
/// </summary>
public static partial class MemoryStats
{
    /// <summary>
    /// Returns physical memory in use and the total usable memory, in bytes, or null if Windows didn't report it.
    /// </summary>
    public static (ulong Used, ulong Total)? Physical()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status)
            ? (status.TotalPhys - status.AvailPhys, status.TotalPhys)
            : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
