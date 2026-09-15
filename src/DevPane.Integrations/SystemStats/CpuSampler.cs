using System.Runtime.InteropServices;

namespace DevPane.Integrations.SystemStats;

/// <summary>
/// Measures total CPU usage across all cores between consecutive calls to <see cref="Sample"/>.
/// </summary>
public sealed partial class CpuSampler
{
    private ulong _lastIdle;
    private ulong _lastTotal;

    public CpuSampler() => TryReadTimes(out _lastIdle, out _lastTotal);

    /// <summary>
    /// Returns CPU usage from 0 to 100 since the previous call, or null if Windows didn't report times.
    /// </summary>
    public double? Sample()
    {
        if (!TryReadTimes(out ulong idle, out ulong total))
        {
            return null;
        }

        ulong idleDelta = idle - _lastIdle;
        ulong totalDelta = total - _lastTotal;
        _lastIdle = idle;
        _lastTotal = total;

        if (totalDelta == 0)
        {
            return null;
        }

        return Math.Clamp(100.0 * (totalDelta - idleDelta) / totalDelta, 0, 100);
    }

    private static bool TryReadTimes(out ulong idle, out ulong total)
    {
        if (GetSystemTimes(out long idleTime, out long kernelTime, out long userTime))
        {
            // Kernel time already includes idle time.
            idle = (ulong)idleTime;
            total = (ulong)(kernelTime + userTime);
            return true;
        }

        idle = 0;
        total = 0;
        return false;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);
}
