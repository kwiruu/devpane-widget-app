using System.Runtime.InteropServices;

namespace DevPane.Integrations.Native;

/// <summary>
/// Thin wrapper over Windows' Performance Data Helper (PDH) API, the source Task Manager uses.
/// One query holds many counters; <see cref="Collect"/> refreshes all of them at once. Not thread-safe.
/// </summary>
internal sealed partial class PdhQuery : IDisposable
{
    private const uint FormatDouble = 0x00000200;
    private const uint FormatNoCap100 = 0x00008000;
    private const uint Format = FormatDouble | FormatNoCap100;
    private const int MoreData = unchecked((int)0x800007D2);

    // PDH_CSTATUS_VALID_DATA and PDH_CSTATUS_NEW_DATA; anything higher means the value can't be used.
    private const uint MaxValidStatus = 1;

    private nint _query;

    public PdhQuery()
    {
        int status = PdhOpenQueryW(null, 0, out _query);
        if (status != 0)
        {
            throw new InvalidOperationException($"PdhOpenQuery failed with 0x{status:X8}");
        }
    }

    /// <summary>
    /// Adds a counter by its English path, such as <c>\Processor Information(_Total)\% Processor Utility</c>.
    /// Returns 0 when the counter doesn't exist on this machine.
    /// </summary>
    public nint TryAddCounter(string englishPath) =>
        PdhAddEnglishCounterW(_query, englishPath, 0, out nint counter) == 0 ? counter : 0;

    public void Collect() => PdhCollectQueryData(_query);

    /// <summary>Reads a single-instance counter. Rate counters return null until two collections have happened.</summary>
    public double? GetValue(nint counter)
    {
        if (counter == 0)
        {
            return null;
        }

        int status = PdhGetFormattedCounterValue(counter, Format, out _, out CounterValue value);
        return status == 0 && value.Status <= MaxValidStatus ? value.Double : null;
    }

    /// <summary>
    /// Reads every instance of a wildcard counter such as <c>\GPU Engine(*)\Utilization Percentage</c>.
    /// Returns null when the counter is unavailable.
    /// </summary>
    public List<(string Instance, double Value)>? GetInstances(nint counter)
    {
        if (counter == 0)
        {
            return null;
        }

        uint bufferSize = 0;
        int status = PdhGetFormattedCounterArrayW(counter, Format, ref bufferSize, out _, 0);
        if (status == 0)
        {
            return [];
        }

        if (status != MoreData || bufferSize == 0)
        {
            return null;
        }

        nint buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            status = PdhGetFormattedCounterArrayW(counter, Format, ref bufferSize, out uint itemCount, buffer);
            if (status != 0)
            {
                return null;
            }

            var results = new List<(string, double)>((int)itemCount);
            int itemSize = Marshal.SizeOf<CounterValueItem>();
            for (int i = 0; i < itemCount; i++)
            {
                var item = Marshal.PtrToStructure<CounterValueItem>(buffer + i * itemSize);
                if (item.Value.Status <= MaxValidStatus)
                {
                    results.Add((Marshal.PtrToStringUni(item.Name) ?? string.Empty, item.Value.Double));
                }
            }

            return results;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (_query != 0)
        {
            PdhCloseQuery(_query);
            _query = 0;
        }
    }

    // PDH_FMT_COUNTERVALUE: a status followed by an 8-byte union; only the double member is used.
    [StructLayout(LayoutKind.Explicit)]
    private struct CounterValue
    {
        [FieldOffset(0)]
        public uint Status;

        [FieldOffset(8)]
        public double Double;
    }

    // PDH_FMT_COUNTERVALUE_ITEM_W
    [StructLayout(LayoutKind.Sequential)]
    private struct CounterValueItem
    {
        public nint Name;
        public CounterValue Value;
    }

    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int PdhOpenQueryW(string? dataSource, nint userData, out nint query);

    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int PdhAddEnglishCounterW(nint query, string fullCounterPath, nint userData, out nint counter);

    [LibraryImport("pdh.dll")]
    private static partial int PdhCollectQueryData(nint query);

    [LibraryImport("pdh.dll")]
    private static partial int PdhGetFormattedCounterValue(nint counter, uint format, out uint type, out CounterValue value);

    [LibraryImport("pdh.dll")]
    private static partial int PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint bufferSize, out uint itemCount, nint itemBuffer);

    [LibraryImport("pdh.dll")]
    private static partial int PdhCloseQuery(nint query);
}
