using System.Runtime.InteropServices;

namespace DevPane.Integrations.LocalDev;

/// <summary>A TCP port a process is listening on.</summary>
/// <param name="IsExposed">True when bound beyond loopback, so other devices on the network may reach it.</param>
internal readonly record struct TcpListener(int Port, int ProcessId, bool IsExposed);

/// <summary>
/// Lists listening TCP ports and their owning processes, like <c>netstat -ano</c>.
/// </summary>
internal static partial class TcpListeners
{
    private const uint AfInet = 2;
    private const uint AfInet6 = 23;
    private const int TcpTableOwnerPidListener = 3;
    private const uint ErrorInsufficientBuffer = 122;

    // MIB_TCPROW_OWNER_PID: state, local address, local port, remote address, remote port, owning PID.
    private const int Ipv4RowSize = 24;

    // MIB_TCP6ROW_OWNER_PID: 16-byte local address, scope ID, local port, remote address and port, state, PID.
    private const int Ipv6RowSize = 56;

    public static List<TcpListener> Read()
    {
        var listeners = new List<TcpListener>();

        ReadTable(AfInet, (buffer, count) =>
        {
            for (int i = 0; i < count; i++)
            {
                int row = sizeof(int) + i * Ipv4RowSize;
                uint address = (uint)Marshal.ReadInt32(buffer, row + 4);
                bool loopback = (address & 0xFF) == 127;
                listeners.Add(new TcpListener(
                    NetworkOrderPort(Marshal.ReadInt32(buffer, row + 8)),
                    Marshal.ReadInt32(buffer, row + 20),
                    !loopback));
            }
        });

        ReadTable(AfInet6, (buffer, count) =>
        {
            for (int i = 0; i < count; i++)
            {
                int row = sizeof(int) + i * Ipv6RowSize;
                listeners.Add(new TcpListener(
                    NetworkOrderPort(Marshal.ReadInt32(buffer, row + 20)),
                    Marshal.ReadInt32(buffer, row + 52),
                    !IsIpv6Loopback(buffer, row)));
            }
        });

        return listeners;
    }

    private static void ReadTable(uint family, Action<nint, int> readRows)
    {
        uint size = 0;
        uint result = GetExtendedTcpTable(0, ref size, false, family, TcpTableOwnerPidListener, 0);

        // The table can grow between the size query and the read, so retry a couple of times.
        for (int attempt = 0; attempt < 3 && result == ErrorInsufficientBuffer; attempt++)
        {
            nint buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                result = GetExtendedTcpTable(buffer, ref size, false, family, TcpTableOwnerPidListener, 0);
                if (result == 0)
                {
                    readRows(buffer, Marshal.ReadInt32(buffer));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    // Ports are stored in network byte order in the low two bytes.
    private static int NetworkOrderPort(int value) => ((value & 0xFF) << 8) | ((value >> 8) & 0xFF);

    // ::1 is fifteen zero bytes followed by 1.
    private static bool IsIpv6Loopback(nint buffer, int row)
    {
        for (int i = 0; i < 15; i++)
        {
            if (Marshal.ReadByte(buffer, row + i) != 0)
            {
                return false;
            }
        }

        return Marshal.ReadByte(buffer, row + 15) == 1;
    }

    [LibraryImport("iphlpapi.dll")]
    private static partial uint GetExtendedTcpTable(nint tcpTable, ref uint size,
        [MarshalAs(UnmanagedType.Bool)] bool order, uint addressFamily, int tableClass, uint reserved);
}
