using DevPane.Integrations.Native;

namespace DevPane.Integrations.SystemStats;

/// <summary>
/// One reading of the machine's CPU, memory, GPU, disk and network. Null means Windows didn't report that value.
/// </summary>
public sealed record SystemSnapshot
{
    public double? CpuPercent { get; init; }

    public ulong? MemoryUsedBytes { get; init; }

    public ulong? MemoryTotalBytes { get; init; }

    public string? GpuName { get; init; }

    public double? GpuPercent { get; init; }

    public ulong? GpuMemoryUsedBytes { get; init; }

    public ulong? GpuMemoryTotalBytes { get; init; }

    /// <summary>True for integrated GPUs, whose memory comes out of system RAM.</summary>
    public bool GpuMemoryIsShared { get; init; }

    /// <summary>Root of the Windows drive, such as <c>C:\</c>.</summary>
    public required string SystemDrive { get; init; }

    public ulong? DiskFreeBytes { get; init; }

    public double? DiskBytesPerSecond { get; init; }

    public double? NetworkReceivedBytesPerSecond { get; init; }

    public double? NetworkSentBytesPerSecond { get; init; }
}

/// <summary>
/// Reads <see cref="SystemSnapshot"/>s. Create once and call <see cref="Sample"/> on an interval: CPU, disk and
/// network activity are averaged over the time since the previous call. Not thread-safe.
/// </summary>
public sealed class SystemSampler : IDisposable
{
    // Below this, a GPU is treated as integrated and its shared memory is reported instead.
    private const ulong MinimumDedicatedMemory = 512UL * 1024 * 1024;

    private readonly PdhQuery _pdh = new();
    private readonly CpuSampler _cpuFallback = new();
    private readonly string _systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
    private readonly Dxgi.Adapter? _gpu;
    private readonly string? _gpuInstancePrefix;
    private readonly nint _cpuUtility;
    private readonly nint _gpuEngineUtilization;
    private readonly nint _gpuDedicatedUsage;
    private readonly nint _gpuSharedUsage;
    private readonly nint _diskBytes;
    private readonly nint _networkReceived;
    private readonly nint _networkSent;

    public SystemSampler()
    {
        _gpu = Dxgi.FindPrimaryGpu();
        if (_gpu is { } gpu)
        {
            // PDH names GPU instances after the adapter LUID, for example "luid_0x00000000_0x0000D1A5_phys_0".
            _gpuInstancePrefix = $"luid_0x{(uint)gpu.LuidHighPart:X8}_0x{gpu.LuidLowPart:X8}";
        }

        // "% Processor Utility" is what Task Manager shows; the older "% Processor Time" reads lower on modern CPUs.
        _cpuUtility = _pdh.TryAddCounter(@"\Processor Information(_Total)\% Processor Utility");
        _gpuEngineUtilization = _pdh.TryAddCounter(@"\GPU Engine(*)\Utilization Percentage");
        _gpuDedicatedUsage = _pdh.TryAddCounter(@"\GPU Adapter Memory(*)\Dedicated Usage");
        _gpuSharedUsage = _pdh.TryAddCounter(@"\GPU Adapter Memory(*)\Shared Usage");
        _diskBytes = _pdh.TryAddCounter(@"\PhysicalDisk(_Total)\Disk Bytes/sec");
        _networkReceived = _pdh.TryAddCounter(@"\Network Interface(*)\Bytes Received/sec");
        _networkSent = _pdh.TryAddCounter(@"\Network Interface(*)\Bytes Sent/sec");

        // Rate counters compare against the previous collection, so take a baseline now.
        _pdh.Collect();
    }

    public SystemSnapshot Sample()
    {
        _pdh.Collect();

        double? cpu = _pdh.GetValue(_cpuUtility) is double utility ? Math.Min(utility, 100) : _cpuFallback.Sample();
        var memory = MemoryStats.Physical();
        var (gpuMemoryUsed, gpuMemoryTotal, gpuMemoryShared) = ReadGpuMemory();

        return new SystemSnapshot
        {
            CpuPercent = cpu,
            MemoryUsedBytes = memory?.Used,
            MemoryTotalBytes = memory?.Total,
            GpuName = _gpu?.Name,
            GpuPercent = ReadGpuUtilization(),
            GpuMemoryUsedBytes = gpuMemoryUsed,
            GpuMemoryTotalBytes = gpuMemoryTotal,
            GpuMemoryIsShared = gpuMemoryShared,
            SystemDrive = _systemDrive,
            DiskFreeBytes = ReadFreeSpace(),
            DiskBytesPerSecond = _pdh.GetValue(_diskBytes),
            NetworkReceivedBytesPerSecond = SumPhysicalNetwork(_networkReceived),
            NetworkSentBytesPerSecond = SumPhysicalNetwork(_networkSent),
        };
    }

    public void Dispose() => _pdh.Dispose();

    // Matches Task Manager: add up every process's use of each GPU engine, then report the busiest engine.
    private double? ReadGpuUtilization()
    {
        var instances = _pdh.GetInstances(_gpuEngineUtilization);
        if (instances is null)
        {
            return null;
        }

        var perEngine = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (instance, value) in instances)
        {
            // Instance names look like "pid_1234_luid_0x00000000_0x0000D1A5_phys_0_eng_3_engtype_3D".
            int luidStart = instance.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
            int typeStart = instance.IndexOf("_engtype_", StringComparison.OrdinalIgnoreCase);
            if (luidStart < 0 || typeStart < luidStart)
            {
                continue;
            }

            string engine = instance[luidStart..typeStart];
            if (_gpuInstancePrefix is null || engine.StartsWith(_gpuInstancePrefix, StringComparison.OrdinalIgnoreCase))
            {
                perEngine[engine] = perEngine.GetValueOrDefault(engine) + value;
            }
        }

        return perEngine.Count == 0 ? 0 : Math.Min(perEngine.Values.Max(), 100);
    }

    private (ulong? Used, ulong? Total, bool Shared) ReadGpuMemory()
    {
        if (_gpu is not { } gpu || _gpuInstancePrefix is null)
        {
            return (null, null, false);
        }

        bool shared = gpu.DedicatedMemory < MinimumDedicatedMemory;
        var instances = _pdh.GetInstances(shared ? _gpuSharedUsage : _gpuDedicatedUsage);
        if (instances is null)
        {
            return (null, null, shared);
        }

        double used = instances
            .Where(i => i.Instance.StartsWith(_gpuInstancePrefix, StringComparison.OrdinalIgnoreCase))
            .Sum(i => i.Value);

        return ((ulong)used, shared ? gpu.SharedMemory : gpu.DedicatedMemory, shared);
    }

    private double? SumPhysicalNetwork(nint counter)
    {
        var instances = _pdh.GetInstances(counter);
        return instances?.Where(i => !IsVirtualAdapter(i.Instance)).Sum(i => i.Value);
    }

    // Hyper-V and WSL virtual switches repeat traffic that also crosses the physical adapter.
    private static bool IsVirtualAdapter(string name) =>
        name.Contains("vEthernet", StringComparison.OrdinalIgnoreCase)
        || name.Contains("isatap", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Teredo", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Loopback", StringComparison.OrdinalIgnoreCase);

    private ulong? ReadFreeSpace()
    {
        try
        {
            return (ulong)new DriveInfo(_systemDrive).AvailableFreeSpace;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
