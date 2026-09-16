using System.Text.Json.Nodes;
using DevPane.Integrations.SystemStats;
using Microsoft.Windows.Widgets;
using Microsoft.Windows.Widgets.Providers;

namespace DevPane.Widgets.Cards;

/// <summary>
/// Live stats in columns, each with its own graph. Small shows CPU, RAM and GPU; medium and large add a
/// second row with disk, download and upload.
/// </summary>
internal sealed class SystemCard : PollingCard<SystemCard.Reading>
{
    public const string DefinitionId = "SystemStats";

    // 30 samples at 3 seconds apart is 90 seconds of history.
    private const int HistoryLength = 30;
    private const int MediumGraphHeight = 28;
    private const int LargeGraphHeight = 44;
    private const double BytesPerMegabyte = 1024d * 1024;

    // Rate counters need two readings a moment apart before they report a value.
    private static readonly TimeSpan BaselineDelay = TimeSpan.FromMilliseconds(500);

    private readonly object _samplerGate = new();
    private readonly History _cpu = new(HistoryLength);
    private readonly History _ram = new(HistoryLength);
    private readonly History _gpu = new(HistoryLength);
    private readonly History _disk = new(HistoryLength);
    private readonly History _download = new(HistoryLength);
    private readonly History _upload = new(HistoryLength);
    private SystemSampler? _sampler;
    private bool _disposed;

    public SystemCard(WidgetContext context, string? customState)
        : base(context, customState)
    {
    }

    /// <summary>The latest stats plus graph history: CPU, RAM and GPU in percent, disk in MB/s, network in Mbps.</summary>
    internal sealed record Reading(
        SystemSnapshot Stats,
        double[] Cpu,
        double[] Ram,
        double[] Gpu,
        double[] Disk,
        double[] Download,
        double[] Upload);

    protected override string TemplateName => DefinitionId;

    protected override TimeSpan Interval => TimeSpan.FromSeconds(3);

    public override void Dispose()
    {
        base.Dispose();
        lock (_samplerGate)
        {
            _disposed = true;
            _sampler?.Dispose();
            _sampler = null;
        }
    }

    protected override ValueTask<Reading> TakeSampleAsync() => ValueTask.FromResult(Sample());

    private Reading Sample()
    {
        lock (_samplerGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_sampler is null)
            {
                // Created on first use, off the Widgets Board's call, because GPU discovery takes a moment.
                _sampler = new SystemSampler();
                Thread.Sleep(BaselineDelay);
            }

            var stats = _sampler.Sample();
            _cpu.Add(stats.CpuPercent);
            _ram.Add(stats.MemoryUsedBytes is ulong used && stats.MemoryTotalBytes is ulong total and > 0
                ? 100.0 * used / total
                : null);
            _gpu.Add(stats.GpuPercent);
            _disk.Add(stats.DiskBytesPerSecond / BytesPerMegabyte);
            _download.Add(stats.NetworkReceivedBytesPerSecond * 8 / 1_000_000);
            _upload.Add(stats.NetworkSentBytesPerSecond * 8 / 1_000_000);

            return new Reading(stats, _cpu.ToArray(), _ram.ToArray(), _gpu.ToArray(),
                _disk.ToArray(), _download.ToArray(), _upload.ToArray());
        }
    }

    protected override JsonObject Describe(Reading? reading)
    {
        var stats = reading?.Stats;
        bool small = Size == WidgetSize.Small;
        int height = Size == WidgetSize.Large ? LargeGraphHeight : MediumGraphHeight;
        var data = new JsonObject
        {
            // Small columns are narrow, so big numbers step down a size and the RAM label drops its unit.
            ["valueSize"] = small ? "large" : "extraLarge",
            ["cpu"] = Format.Percent(stats?.CpuPercent),
            ["cpuDetail"] = $"{Environment.ProcessorCount} threads",
            ["ramLabel"] = small ? "RAM" : "RAM (GB)",
            ["ram"] = stats?.MemoryUsedBytes is ulong used ? Format.GigabytesNumber(used) : Format.Missing,
            ["ramDetail"] = stats?.MemoryTotalBytes is ulong total ? $"of {Format.GigabytesNumber(total)}" : string.Empty,
            ["hasGpu"] = stats?.GpuPercent is not null,
            ["gpu"] = Format.Percent(stats?.GpuPercent),
            ["gpuDetail"] = DescribeGpuMemory(stats),
            ["cpuGraph"] = Sparkline.ToDataUri(reading?.Cpu ?? [], StatColors.Cpu, height, max: 100),
            ["ramGraph"] = Sparkline.ToDataUri(reading?.Ram ?? [], StatColors.Ram, height, max: 100),
            ["gpuGraph"] = Sparkline.ToDataUri(reading?.Gpu ?? [], StatColors.Gpu, height, max: 100),
        };

        // Small only shows the top row, so skip the second row's data and graphs.
        if (!small)
        {
            data["disk"] = Format.MegabytesPerSecond(stats?.DiskBytesPerSecond);
            data["diskDetail"] = stats?.DiskFreeBytes is ulong free ? $"{Format.Memory(free)} free" : string.Empty;
            data["download"] = Format.Megabits(stats?.NetworkReceivedBytesPerSecond);
            data["upload"] = Format.Megabits(stats?.NetworkSentBytesPerSecond);
            data["diskGraph"] = Sparkline.ToDataUri(reading?.Disk ?? [], StatColors.Disk, height, max: null);
            data["downloadGraph"] = Sparkline.ToDataUri(reading?.Download ?? [], StatColors.Download, height, max: null);
            data["uploadGraph"] = Sparkline.ToDataUri(reading?.Upload ?? [], StatColors.Upload, height, max: null);
        }

        return data;
    }

    private static string DescribeGpuMemory(SystemSnapshot? stats) =>
        stats?.GpuMemoryUsedBytes is ulong used && stats.GpuMemoryTotalBytes is ulong total
            ? $"{Format.GigabytesNumber(used)} of {Format.GigabytesNumber(total)} GB"
            : string.Empty;

    // One color per stat so each graph is recognizable at a glance.
    private static class StatColors
    {
        public const string Cpu = "#3B8FD9";
        public const string Ram = "#9B6FE0";
        public const string Gpu = "#2FA66F";
        public const string Disk = "#D9912B";
        public const string Download = "#1FA3A8";
        public const string Upload = "#D95D8C";
    }

    // Fixed-length history of recent samples; missing readings are skipped.
    private sealed class History(int capacity)
    {
        private readonly Queue<double> _values = new(capacity);

        public void Add(double? value)
        {
            if (value is not double v)
            {
                return;
            }

            _values.Enqueue(v);
            if (_values.Count > capacity)
            {
                _values.Dequeue();
            }
        }

        public double[] ToArray() => _values.ToArray();
    }
}
